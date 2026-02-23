using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LMSupply.Runtime;

/// <summary>
/// Provides cross-platform detection and configuration of CUDA and cuDNN environments.
/// Uses environment variables as primary detection mechanism with fallback to standard paths.
/// Supports Windows, Linux, and macOS.
/// </summary>
public sealed class CudaEnvironment
{
    private static CudaEnvironment? _instance;
    private static readonly object _lock = new();

    private readonly List<CudaInstallation> _cudaInstallations = new();
    private readonly List<CuDnnInstallation> _cudnnInstallations = new();
    private CudaInstallation? _primaryCuda;
    private bool _isInitialized;

    /// <summary>
    /// Gets the singleton instance of CudaEnvironment.
    /// </summary>
    public static CudaEnvironment Instance
    {
        get
        {
            if (_instance is null)
            {
                lock (_lock)
                {
                    _instance ??= new CudaEnvironment();
                }
            }
            return _instance;
        }
    }

    private CudaEnvironment()
    {
    }

    /// <summary>
    /// Initializes the CUDA environment detection.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        lock (_lock)
        {
            if (_isInitialized) return;

            DetectCudaInstallations();
            DetectCuDnnInstallations();
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Gets all detected CUDA installations.
    /// </summary>
    public IReadOnlyList<CudaInstallation> CudaInstallations
    {
        get
        {
            Initialize();
            return _cudaInstallations;
        }
    }

    /// <summary>
    /// Gets all detected cuDNN installations.
    /// </summary>
    public IReadOnlyList<CuDnnInstallation> CuDnnInstallations
    {
        get
        {
            Initialize();
            return _cudnnInstallations;
        }
    }

    /// <summary>
    /// Gets the primary CUDA installation (from CUDA_PATH or CUDAToolkit_ROOT environment variable).
    /// </summary>
    public CudaInstallation? PrimaryCuda
    {
        get
        {
            Initialize();
            return _primaryCuda;
        }
    }

    /// <summary>
    /// Gets the best matching cuDNN installation for the given CUDA version.
    /// </summary>
    public CuDnnInstallation? GetBestCuDnn(int cudaMajorVersion)
    {
        Initialize();
        return _cudnnInstallations
            .Where(c => c.CudaMajorVersion == cudaMajorVersion)
            .OrderByDescending(c => c.Version)
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks if CUDA runtime libraries are available for the specified CUDA major version.
    /// </summary>
    public (bool Available, string[] MissingLibraries) CheckCudaLibraries(int cudaMajorVersion)
    {
        Initialize();
        var missing = new List<string>();

        var cuda = _cudaInstallations.FirstOrDefault(c => c.MajorVersion == cudaMajorVersion);
        if (cuda is null)
        {
            return (false, new[] { $"CUDA {cudaMajorVersion}.x not found" });
        }

        // Check for required cuBLAS libraries
        var cublasLibs = GetCublasLibraryNames(cudaMajorVersion);
        foreach (var lib in cublasLibs)
        {
            if (!cuda.LibraryPaths.Any(p => File.Exists(Path.Combine(p, lib))))
            {
                missing.Add(lib);
            }
        }

        return missing.Count == 0 ? (true, Array.Empty<string>()) : (false, missing.ToArray());
    }

    /// <summary>
    /// Checks if cuDNN is available and properly configured for the specified CUDA version.
    /// </summary>
    public (bool Available, string[] MissingLibraries, string? LibPath) CheckCuDnnLibraries(int cudaMajorVersion)
    {
        Initialize();
        var missing = new List<string>();

        var cudnn = GetBestCuDnn(cudaMajorVersion);
        if (cudnn is null)
        {
            return (false, new[] { $"cuDNN for CUDA {cudaMajorVersion}.x not found" }, null);
        }

        // Check for main cuDNN library
        var cudnnLib = GetCuDnnLibraryName(cudnn.MajorVersion);
        if (!File.Exists(Path.Combine(cudnn.LibraryPath, cudnnLib)))
        {
            missing.Add(cudnnLib);
        }

        // Note: zlibwapi.dll is NOT required for cuDNN 9.x on Windows (statically linked)
        // For cuDNN 8.x on Windows or Linux, zlib may still be needed
        if (cudnn.MajorVersion < 9 || !OperatingSystem.IsWindows())
        {
            var zlibFound = CheckZlibAvailability(cudnn.LibraryPath);
            if (!zlibFound && cudnn.MajorVersion >= 8)
            {
                var zlibName = OperatingSystem.IsWindows() ? "zlibwapi.dll" : "libz.so";
                missing.Add($"{zlibName} (may be required for cuDNN {cudnn.MajorVersion}.x)");
            }
        }

        return missing.Count == 0
            ? (true, Array.Empty<string>(), cudnn.LibraryPath)
            : (false, missing.ToArray(), cudnn.LibraryPath);
    }

    /// <summary>
    /// Gets all paths that should be added to the library search path for CUDA/cuDNN.
    /// </summary>
    public IEnumerable<string> GetDllSearchPaths(int cudaMajorVersion)
    {
        Initialize();
        var paths = new List<string>();

        // Add CUDA library paths
        var cuda = _cudaInstallations.FirstOrDefault(c => c.MajorVersion == cudaMajorVersion);
        if (cuda is not null)
        {
            paths.AddRange(cuda.LibraryPaths.Where(Directory.Exists));
        }

        // Add cuDNN paths (prioritize matching CUDA version)
        var matchingCudnn = _cudnnInstallations
            .Where(c => c.CudaMajorVersion == cudaMajorVersion)
            .OrderByDescending(c => c.Version);

        foreach (var cudnn in matchingCudnn)
        {
            if (Directory.Exists(cudnn.LibraryPath))
            {
                paths.Add(cudnn.LibraryPath);
            }
        }

        // Also add other cuDNN versions as fallback
        var otherCudnn = _cudnnInstallations
            .Where(c => c.CudaMajorVersion != cudaMajorVersion)
            .OrderByDescending(c => c.Version);

        foreach (var cudnn in otherCudnn)
        {
            if (Directory.Exists(cudnn.LibraryPath))
            {
                paths.Add(cudnn.LibraryPath);
            }
        }

        return paths.Distinct();
    }

    /// <summary>
    /// Gets diagnostic information about the CUDA/cuDNN environment.
    /// </summary>
    public string GetDiagnostics()
    {
        Initialize();
        var sb = new System.Text.StringBuilder();
        var platform = GetPlatformName();

        sb.AppendLine(CultureInfo.InvariantCulture, $"=== CUDA Environment Diagnostics ({platform}) ===");
        sb.AppendLine();

        // Environment variables
        sb.AppendLine("Environment Variables:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  CUDA_PATH: {Environment.GetEnvironmentVariable("CUDA_PATH") ?? "(not set)"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  CUDAToolkit_ROOT: {Environment.GetEnvironmentVariable("CUDAToolkit_ROOT") ?? "(not set)"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  CUDNN_PATH: {Environment.GetEnvironmentVariable("CUDNN_PATH") ?? "(not set)"}");
        if (!OperatingSystem.IsWindows())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  LD_LIBRARY_PATH: {Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "(not set)"}");
        }
        sb.AppendLine();

        sb.AppendLine("CUDA Installations:");
        if (_cudaInstallations.Count == 0)
        {
            sb.AppendLine("  (none found)");
        }
        else
        {
            foreach (var cuda in _cudaInstallations)
            {
                var isPrimary = cuda == _primaryCuda ? " [PRIMARY]" : "";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - CUDA {cuda.Version} at {cuda.Path}{isPrimary}");
                foreach (var libPath in cuda.LibraryPaths.Where(Directory.Exists))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    lib: {libPath}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("cuDNN Installations:");
        if (_cudnnInstallations.Count == 0)
        {
            sb.AppendLine("  (none found)");
        }
        else
        {
            foreach (var cudnn in _cudnnInstallations)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - cuDNN {cudnn.Version} for CUDA {cudnn.CudaMajorVersion}.x");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    lib: {cudnn.LibraryPath}");

                // Only show zlib status for cuDNN 8.x or non-Windows
                if (cudnn.MajorVersion < 9 || !OperatingSystem.IsWindows())
                {
                    var zlibStatus = CheckZlibAvailability(cudnn.LibraryPath) ? "found" : "not found";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    zlib: {zlibStatus}");
                }
            }
        }

        return sb.ToString();
    }

    #region Detection Methods

    private void DetectCudaInstallations()
    {
        // 1. Primary: CUDA_PATH environment variable (Windows standard)
        TryAddCudaFromEnvVar("CUDA_PATH", isPrimary: true);

        // 2. CUDAToolkit_ROOT environment variable (CMake/TensorFlow standard)
        TryAddCudaFromEnvVar("CUDAToolkit_ROOT", isPrimary: _primaryCuda is null);

        // 3. Versioned CUDA paths: CUDA_PATH_V{major}_{minor} (Windows CUDA installer)
        var envVars = Environment.GetEnvironmentVariables();
        foreach (string key in envVars.Keys)
        {
            if (key.StartsWith("CUDA_PATH_V", StringComparison.OrdinalIgnoreCase))
            {
                var path = envVars[key]?.ToString();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    var version = ParseCudaVersionFromEnvVar(key) ?? DetectCudaVersionFromPath(path);
                    if (version is not null && !_cudaInstallations.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        _cudaInstallations.Add(new CudaInstallation(path, version));
                    }
                }
            }
        }

        // 4. Platform-specific standard paths (fallback)
        foreach (var standardPath in GetStandardCudaPaths())
        {
            if (Directory.Exists(standardPath) && !_cudaInstallations.Any(c => c.Path.Equals(standardPath, StringComparison.OrdinalIgnoreCase)))
            {
                var version = DetectCudaVersionFromPath(standardPath);
                if (version is not null)
                {
                    _cudaInstallations.Add(new CudaInstallation(standardPath, version));
                }
            }
        }

        // Sort by version descending
        _cudaInstallations.Sort((a, b) => b.Version.CompareTo(a.Version));
    }

    private void TryAddCudaFromEnvVar(string envVarName, bool isPrimary)
    {
        var path = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        // Already added?
        if (_cudaInstallations.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var version = DetectCudaVersionFromPath(path);
        if (version is null)
            return;

        var installation = new CudaInstallation(path, version);
        _cudaInstallations.Add(installation);

        if (isPrimary)
        {
            _primaryCuda = installation;
        }
    }

    private void DetectCuDnnInstallations()
    {
        // 1. CUDNN_PATH environment variable (CMake/official standard)
        TryAddCuDnnFromEnvVar("CUDNN_PATH");

        // 2. CUDNN_INSTALL_PATH (TensorFlow standard)
        TryAddCuDnnFromEnvVar("CUDNN_INSTALL_PATH");

        // 3. Check cuDNN in CUDA installation directories
        foreach (var cuda in _cudaInstallations)
        {
            TryAddCuDnnFromCudaPath(cuda);
        }

        // 4. Platform-specific standard paths (fallback)
        foreach (var basePath in GetStandardCuDnnBasePaths())
        {
            if (!Directory.Exists(basePath))
                continue;

            // Scan version directories (v8.x, v9.x, etc.)
            foreach (var versionDir in Directory.GetDirectories(basePath, "v*"))
            {
                TryAddCuDnnFromVersionDir(versionDir);
            }
        }

        // 5. Check LD_LIBRARY_PATH directories on Linux/macOS
        if (!OperatingSystem.IsWindows())
        {
            TryAddCuDnnFromLibraryPath();
        }

        // Sort by version descending, then by CUDA version match
        _cudnnInstallations.Sort((a, b) =>
        {
            var versionCompare = b.Version.CompareTo(a.Version);
            return versionCompare != 0 ? versionCompare : b.CudaMajorVersion.CompareTo(a.CudaMajorVersion);
        });
    }

    private void TryAddCuDnnFromEnvVar(string envVarName)
    {
        var path = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        // Detect cuDNN version and CUDA compatibility
        var (cudnnVersion, cudaMajor, libPath) = DetectCuDnnFromPath(path);
        if (cudnnVersion is null || libPath is null)
            return;

        if (!_cudnnInstallations.Any(c => c.LibraryPath.Equals(libPath, StringComparison.OrdinalIgnoreCase)))
        {
            _cudnnInstallations.Add(new CuDnnInstallation(libPath, cudnnVersion, cudaMajor));
        }
    }

    private void TryAddCuDnnFromCudaPath(CudaInstallation cuda)
    {
        // Check if cuDNN is installed alongside CUDA
        foreach (var libPath in cuda.LibraryPaths)
        {
            foreach (var cudnnMajor in new[] { 9, 8, 7 })
            {
                var cudnnLib = GetCuDnnLibraryName(cudnnMajor);
                if (File.Exists(Path.Combine(libPath, cudnnLib)))
                {
                    var version = DetectCuDnnVersionFromLibrary(libPath, cudnnMajor) ?? new Version(cudnnMajor, 0);
                    if (!_cudnnInstallations.Any(c => c.LibraryPath.Equals(libPath, StringComparison.OrdinalIgnoreCase) && c.MajorVersion == cudnnMajor))
                    {
                        _cudnnInstallations.Add(new CuDnnInstallation(libPath, version, cuda.MajorVersion));
                    }
                    break; // Found cuDNN in this path
                }
            }
        }
    }

    private void TryAddCuDnnFromVersionDir(string versionDir)
    {
        var cudnnVersion = ParseVersionFromDirName(Path.GetFileName(versionDir));
        if (cudnnVersion is null)
            return;

        // Check multiple possible layouts
        var possibleLibPaths = GetPossibleCuDnnLibPaths(versionDir);

        foreach (var libPath in possibleLibPaths)
        {
            if (!Directory.Exists(libPath))
                continue;

            var cudnnLib = GetCuDnnLibraryName(cudnnVersion.Major);
            if (File.Exists(Path.Combine(libPath, cudnnLib)))
            {
                // Try to determine CUDA version from path structure
                var cudaMajor = DetectCudaMajorFromCuDnnPath(libPath) ?? _primaryCuda?.MajorVersion ?? 12;

                if (!_cudnnInstallations.Any(c => c.LibraryPath.Equals(libPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _cudnnInstallations.Add(new CuDnnInstallation(libPath, cudnnVersion, cudaMajor));
                }
            }
        }
    }

    private void TryAddCuDnnFromLibraryPath()
    {
        var ldLibPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")
                     ?? Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");

        if (string.IsNullOrEmpty(ldLibPath))
            return;

        foreach (var dir in ldLibPath.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                continue;

            foreach (var cudnnMajor in new[] { 9, 8, 7 })
            {
                var cudnnLib = GetCuDnnLibraryName(cudnnMajor);
                if (File.Exists(Path.Combine(dir, cudnnLib)))
                {
                    var version = DetectCuDnnVersionFromLibrary(dir, cudnnMajor) ?? new Version(cudnnMajor, 0);
                    var cudaMajor = _primaryCuda?.MajorVersion ?? 12;

                    if (!_cudnnInstallations.Any(c => c.LibraryPath.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                    {
                        _cudnnInstallations.Add(new CuDnnInstallation(dir, version, cudaMajor));
                    }
                    break;
                }
            }
        }
    }

    #endregion

    #region Path Discovery Helpers

    private static IEnumerable<string> GetStandardCudaPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: NVIDIA GPU Computing Toolkit
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var nvidiaPath = Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA");
            if (Directory.Exists(nvidiaPath))
            {
                foreach (var versionDir in Directory.GetDirectories(nvidiaPath, "v*"))
                {
                    yield return versionDir;
                }
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux: /usr/local/cuda, /usr/local/cuda-XX.X, /opt/cuda
            yield return "/usr/local/cuda";

            if (Directory.Exists("/usr/local"))
            {
                foreach (var dir in Directory.GetDirectories("/usr/local", "cuda-*"))
                {
                    yield return dir;
                }
            }

            yield return "/opt/cuda";
        }
        else if (OperatingSystem.IsMacOS())
        {
            // macOS: /usr/local/cuda, /Developer/NVIDIA/CUDA-*
            yield return "/usr/local/cuda";

            if (Directory.Exists("/Developer/NVIDIA"))
            {
                foreach (var dir in Directory.GetDirectories("/Developer/NVIDIA", "CUDA-*"))
                {
                    yield return dir;
                }
            }
        }
    }

    private static IEnumerable<string> GetStandardCuDnnBasePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "NVIDIA", "CUDNN");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA", "CUDNN");
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "/usr/local/cudnn";
            yield return "/opt/cudnn";
            yield return "/usr/lib/x86_64-linux-gnu"; // Debian/Ubuntu
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/cudnn";
            yield return "/opt/cudnn";
        }
    }

    private static IEnumerable<string> GetPossibleCuDnnLibPaths(string cuDnnRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            var binDir = Path.Combine(cuDnnRoot, "bin");
            if (Directory.Exists(binDir))
            {
                // Direct: bin/cudnn64_9.dll
                yield return binDir;

                // CUDA version specific: bin/{cuda_version}/cudnn64_9.dll or bin/{cuda_version}/x64/cudnn64_9.dll
                foreach (var subDir in Directory.GetDirectories(binDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    // Check if it's a version directory (e.g., "12.9", "13.1")
                    if (Version.TryParse(dirName, out _) || int.TryParse(dirName, out _))
                    {
                        yield return subDir;

                        // Architecture subdirectory (x64/x86)
                        var archDir = Environment.Is64BitProcess ? "x64" : "x86";
                        yield return Path.Combine(subDir, archDir);
                    }
                }
            }
        }
        else
        {
            // Linux/macOS: lib64, lib, lib/x86_64-linux-gnu
            yield return Path.Combine(cuDnnRoot, "lib64");
            yield return Path.Combine(cuDnnRoot, "lib");
            yield return Path.Combine(cuDnnRoot, "lib", "x86_64-linux-gnu");
        }
    }

    #endregion

    #region Version Detection Helpers

    private static Version? DetectCudaVersionFromPath(string cudaPath)
    {
        // Try to parse from directory name (e.g., "v12.9", "cuda-12.9")
        var dirName = Path.GetFileName(cudaPath.TrimEnd(Path.DirectorySeparatorChar));
        var version = ParseVersionFromDirName(dirName);
        if (version is not null)
            return version;

        // Try to read version from version.txt or version.json
        var versionFile = Path.Combine(cudaPath, "version.txt");
        if (File.Exists(versionFile))
        {
            try
            {
                var content = File.ReadAllText(versionFile).Trim();
                // Format: "CUDA Version 12.9.0" or just "12.9.0"
                var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+\.\d+(\.\d+)?)");
                if (match.Success && Version.TryParse(match.Value, out var v))
                    return v;
            }
            catch (Exception ex)
            {
                Trace.TraceInformation($"[CUDA] Failed to read version.txt: {ex.Message}");
            }
        }

        // Try nvcc --version as fallback (expensive, only if necessary)
        var nvccPath = OperatingSystem.IsWindows()
            ? Path.Combine(cudaPath, "bin", "nvcc.exe")
            : Path.Combine(cudaPath, "bin", "nvcc");

        if (File.Exists(nvccPath))
        {
            try
            {
                var psi = new ProcessStartInfo(nvccPath, "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process is not null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    // Parse "release 12.9" or "V12.9.0"
                    var match = System.Text.RegularExpressions.Regex.Match(output, @"(?:release|V)\s*(\d+\.\d+(\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && Version.TryParse(match.Groups[1].Value, out var v))
                        return v;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation($"[CUDA] Failed to run nvcc: {ex.Message}");
            }
        }

        return null;
    }

    private static (Version? Version, int CudaMajor, string? LibPath) DetectCuDnnFromPath(string cuDnnPath)
    {
        var possibleLibPaths = GetPossibleCuDnnLibPaths(cuDnnPath).ToList();

        // Also check root directly
        possibleLibPaths.Insert(0, cuDnnPath);

        foreach (var libPath in possibleLibPaths)
        {
            if (!Directory.Exists(libPath))
                continue;

            foreach (var cudnnMajor in new[] { 9, 8, 7 })
            {
                var cudnnLib = GetCuDnnLibraryName(cudnnMajor);
                if (File.Exists(Path.Combine(libPath, cudnnLib)))
                {
                    var version = DetectCuDnnVersionFromLibrary(libPath, cudnnMajor) ?? new Version(cudnnMajor, 0);
                    var cudaMajor = DetectCudaMajorFromCuDnnPath(libPath) ?? 12;
                    return (version, cudaMajor, libPath);
                }
            }
        }

        return (null, 0, null);
    }

    private static Version? DetectCuDnnVersionFromLibrary(string libPath, int expectedMajor)
    {
        // Try to find version from cudnn_version.h or include/cudnn_version.h
        var headerPaths = new[]
        {
            Path.Combine(libPath, "..", "include", "cudnn_version.h"),
            Path.Combine(libPath, "..", "..", "include", "cudnn_version.h"),
        };

        foreach (var headerPath in headerPaths)
        {
            if (!File.Exists(headerPath))
                continue;

            try
            {
                var content = File.ReadAllText(headerPath);
                var majorMatch = System.Text.RegularExpressions.Regex.Match(content, @"CUDNN_MAJOR\s+(\d+)");
                var minorMatch = System.Text.RegularExpressions.Regex.Match(content, @"CUDNN_MINOR\s+(\d+)");
                var patchMatch = System.Text.RegularExpressions.Regex.Match(content, @"CUDNN_PATCHLEVEL\s+(\d+)");

                if (majorMatch.Success && minorMatch.Success)
                {
                    var major = int.Parse(majorMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    var minor = int.Parse(minorMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    var patch = patchMatch.Success ? int.Parse(patchMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                    return new Version(major, minor, patch);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation($"[CUDA] Failed to read cudnn_version.h: {ex.Message}");
            }
        }

        return null;
    }

    private static int? DetectCudaMajorFromCuDnnPath(string libPath)
    {
        // Try to extract CUDA version from path like "bin/12.9/x64" or "bin/12"
        var parts = libPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts.Reverse())
        {
            if (Version.TryParse(part, out var v))
                return v.Major;
            if (int.TryParse(part, out var major) && major >= 9 && major <= 20)
                return major;
        }
        return null;
    }

    private static Version? ParseCudaVersionFromEnvVar(string envVarName)
    {
        // Extract version from "CUDA_PATH_V12_9" -> 12.9
        if (!envVarName.StartsWith("CUDA_PATH_V", StringComparison.OrdinalIgnoreCase))
            return null;

        var versionPart = envVarName.Substring(11).Replace('_', '.');
        return Version.TryParse(versionPart, out var version) ? version : null;
    }

    private static Version? ParseVersionFromDirName(string dirName)
    {
        // Remove common prefixes: "v", "cuda-", "CUDA-", "cudnn-"
        var cleaned = dirName;
        foreach (var prefix in new[] { "v", "cuda-", "CUDA-", "cudnn-", "cuDNN-" })
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(prefix.Length);
                break;
            }
        }

        return Version.TryParse(cleaned, out var version) ? version : null;
    }

    #endregion

    #region Library Name Helpers

    /// <summary>
    /// Gets the cuBLAS library names for the specified CUDA major version.
    /// </summary>
    public static string[] GetCublasLibraryNames(int cudaMajorVersion)
    {
        if (OperatingSystem.IsWindows())
        {
            return new[]
            {
                $"cublas64_{cudaMajorVersion}.dll",
                $"cublasLt64_{cudaMajorVersion}.dll"
            };
        }
        else
        {
            return new[]
            {
                $"libcublas.so.{cudaMajorVersion}",
                $"libcublasLt.so.{cudaMajorVersion}"
            };
        }
    }

    /// <summary>
    /// Gets the cuDNN main library name for the specified cuDNN major version.
    /// </summary>
    public static string GetCuDnnLibraryName(int cudnnMajorVersion)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"cudnn64_{cudnnMajorVersion}.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            return $"libcudnn.{cudnnMajorVersion}.dylib";
        }
        else
        {
            return $"libcudnn.so.{cudnnMajorVersion}";
        }
    }

    /// <summary>
    /// Gets all cuDNN component library names for the specified version.
    /// </summary>
    public static string[] GetCuDnnComponentLibraries(int cudnnMajorVersion)
    {
        var ext = OperatingSystem.IsWindows() ? ".dll"
                : OperatingSystem.IsMacOS() ? ".dylib"
                : ".so";
        var prefix = OperatingSystem.IsWindows() ? "" : "lib";
        var suffix = OperatingSystem.IsWindows() ? $"64_{cudnnMajorVersion}" : $".{cudnnMajorVersion}";

        return new[]
        {
            $"{prefix}cudnn{suffix}{ext}",
            $"{prefix}cudnn_adv{suffix}{ext}",
            $"{prefix}cudnn_cnn{suffix}{ext}",
            $"{prefix}cudnn_ops{suffix}{ext}",
            $"{prefix}cudnn_graph{suffix}{ext}",
            $"{prefix}cudnn_engines_precompiled{suffix}{ext}",
            $"{prefix}cudnn_engines_runtime_compiled{suffix}{ext}",
            $"{prefix}cudnn_heuristic{suffix}{ext}",
        };
    }

    private bool CheckZlibAvailability(string libPath)
    {
        var zlibName = OperatingSystem.IsWindows() ? "zlibwapi.dll" : "libz.so";

        // Check in cuDNN directory
        if (File.Exists(Path.Combine(libPath, zlibName)))
            return true;

        // Check in CUDA directories
        foreach (var cuda in _cudaInstallations)
        {
            foreach (var cudaLibPath in cuda.LibraryPaths)
            {
                if (File.Exists(Path.Combine(cudaLibPath, zlibName)))
                    return true;
            }
        }

        // On Linux, zlib is usually in system paths
        if (!OperatingSystem.IsWindows())
        {
            foreach (var systemPath in new[] { "/usr/lib", "/usr/lib64", "/lib", "/lib64", "/usr/lib/x86_64-linux-gnu" })
            {
                if (File.Exists(Path.Combine(systemPath, "libz.so")) ||
                    File.Exists(Path.Combine(systemPath, "libz.so.1")))
                    return true;
            }
        }

        // Check in PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, zlibName)))
                return true;
        }

        return false;
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsLinux()) return "Linux";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return "Unknown";
    }

    #endregion
}

/// <summary>
/// Represents a detected CUDA installation.
/// </summary>
public sealed class CudaInstallation
{
    public string Path { get; }
    public Version Version { get; }
    public int MajorVersion => Version.Major;

    /// <summary>
    /// Gets the library paths for this CUDA installation (bin on Windows, lib64/lib on Linux/macOS).
    /// </summary>
    public IReadOnlyList<string> LibraryPaths { get; }

    public CudaInstallation(string path, Version version)
    {
        Path = path;
        Version = version;
        LibraryPaths = DiscoverLibraryPaths(path);
    }

    private static List<string> DiscoverLibraryPaths(string cudaPath)
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            paths.Add(System.IO.Path.Combine(cudaPath, "bin"));
        }
        else
        {
            // Linux/macOS: prefer lib64 over lib
            var lib64 = System.IO.Path.Combine(cudaPath, "lib64");
            var lib = System.IO.Path.Combine(cudaPath, "lib");

            if (Directory.Exists(lib64))
                paths.Add(lib64);
            if (Directory.Exists(lib))
                paths.Add(lib);
        }

        return paths;
    }

    public override string ToString() => $"CUDA {Version} at {Path}";
}

/// <summary>
/// Represents a detected cuDNN installation.
/// </summary>
public sealed class CuDnnInstallation
{
    /// <summary>
    /// Gets the library path where cuDNN DLLs/SOs are located.
    /// </summary>
    public string LibraryPath { get; }

    public Version Version { get; }
    public int MajorVersion => Version.Major;
    public int CudaMajorVersion { get; }

    public CuDnnInstallation(string libraryPath, Version version, int cudaMajorVersion)
    {
        LibraryPath = libraryPath;
        Version = version;
        CudaMajorVersion = cudaMajorVersion;
    }

    public override string ToString() => $"cuDNN {Version} (CUDA {CudaMajorVersion}.x) at {LibraryPath}";
}
