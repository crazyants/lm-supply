import { useState } from 'react';
import { Terminal, ChevronDown, ChevronRight, Copy, Check } from 'lucide-react';

interface CurlCommand {
  label: string;
  command: string;
}

interface CurlExampleProps {
  commands: CurlCommand[];
}

export function CurlExample({ commands }: CurlExampleProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [copiedIndex, setCopiedIndex] = useState<number | null>(null);

  const handleCopy = async (command: string, index: number) => {
    await navigator.clipboard.writeText(command);
    setCopiedIndex(index);
    setTimeout(() => setCopiedIndex(null), 2000);
  };

  return (
    <div className="border border-border rounded-lg">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center gap-2 px-4 py-2 text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        <Terminal className="w-4 h-4" />
        <span className="font-medium">cURL Examples</span>
        {isOpen ? (
          <ChevronDown className="w-4 h-4 ml-auto" />
        ) : (
          <ChevronRight className="w-4 h-4 ml-auto" />
        )}
      </button>
      {isOpen && (
        <div className="border-t border-border p-4 space-y-3">
          {commands.map((cmd, i) => (
            <div key={i}>
              {commands.length > 1 && (
                <p className="text-xs font-medium text-muted-foreground mb-1">{cmd.label}</p>
              )}
              <div className="relative group">
                <pre className="bg-muted rounded p-3 text-xs font-mono overflow-x-auto whitespace-pre-wrap break-all">
                  {cmd.command}
                </pre>
                <button
                  onClick={() => handleCopy(cmd.command, i)}
                  className="absolute top-2 right-2 p-1 rounded bg-background/80 opacity-0 group-hover:opacity-100 transition-opacity"
                  title="Copy"
                >
                  {copiedIndex === i ? (
                    <Check className="w-3.5 h-3.5 text-green-500" />
                  ) : (
                    <Copy className="w-3.5 h-3.5" />
                  )}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
