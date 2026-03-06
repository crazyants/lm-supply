# API Key 기능 설계

**날짜**: 2026-03-06
**대상**: `console/host` (ASP.NET Core) + `console/ui` (React)

## 개요

Console Host에 선택적 API Key 인증을 추가한다. 키가 하나도 없으면 무제한 접근, 키가 하나라도 생성되면 모든 AI 엔드포인트에 유효한 Bearer 토큰이 필요하다. 키는 여러 개 생성 가능하며 삭제 가능하다. 요청 로그와 통계를 SQLite에 저장한다.

## 용어

- **API Key** (api-token 아님): OpenAI 관례와 일치. 단순 불투명 자격증명.

## 키 형식

```
lms-<32자 소문자 hex>
예: lms-a1b2c3d4e5f67890abcdef1234567890
```

- `lms-` 접두사로 일반 Bearer 토큰과 구분
- 생성 시 1회만 평문 노출, 이후 SHA-256 해시만 저장
- 표시용 prefix: 첫 12자 (`lms-a1b2c3d4`) + `****`

## 아키텍처

### 인증 규칙

| DB 상태 | 요청 헤더 | 결과 |
|---------|-----------|------|
| 키 0개 | 없음 | 통과 (무제한) |
| 키 0개 | 있음 | 통과 (무시) |
| 키 1개 이상 | 유효한 Bearer | 통과 + 로깅 |
| 키 1개 이상 | 없거나 유효하지 않음 | 401 |

**예외**: `/api/keys/*` 관리 엔드포인트는 항상 인증 없이 접근 가능.

### 미들웨어 파이프라인

```
RequestIdMiddleware
  → ApiKeyMiddleware  ← 신규 추가
    → ErrorMiddleware
      → endpoints
```

### 저장소

- 경로: `~/.lmsupply/api-keys.db`
- EF Core SQLite 사용

## 데이터 모델

### ApiKeys 테이블

| 컬럼 | 타입 | 설명 |
|------|------|------|
| Id | Guid (PK) | |
| Name | string | 사용자 지정 이름 |
| KeyHash | string | SHA-256(full key) |
| KeyPrefix | string | 표시용 (`lms-a1b2c3d4`) |
| CreatedAt | DateTime | UTC |
| LastUsedAt | DateTime? | UTC, 요청마다 갱신 |
| TotalRequests | long | 비정규화 카운터 (빠른 목록 조회용) |

### ApiKeyRequests 테이블

| 컬럼 | 타입 | 설명 |
|------|------|------|
| Id | long (PK, autoincrement) | |
| ApiKeyId | Guid (FK) | |
| Timestamp | DateTime | UTC |
| Path | string | 요청 경로 |
| Method | string | HTTP 메서드 |
| StatusCode | int | |
| DurationMs | long | 응답시간 (ms) |

- 30일이 지난 로그는 자동 정리 (앱 시작 시 1회)

## 백엔드

### 신규 파일

```
console/host/
├── Infrastructure/
│   └── ApiKeyMiddleware.cs        # 인증 + 요청 로깅
├── Services/
│   └── ApiKeyService.cs           # CRUD + 통계 쿼리
├── Data/
│   ├── ApiKeyDbContext.cs         # EF Core DbContext
│   └── Migrations/                # EF 마이그레이션
├── Models/
│   └── ApiKeyModels.cs            # DB 엔티티 + Request/Response DTO
└── Endpoints/
    └── ApiKeyEndpoints.cs         # /api/keys/* 라우팅
```

### API 엔드포인트

| 메서드 | 경로 | 요청 | 응답 |
|--------|------|------|------|
| `GET` | `/api/keys` | — | 키 목록 (해시 제외) |
| `POST` | `/api/keys` | `{ "name": "..." }` | 키 정보 + **풀 키 1회** |
| `DELETE` | `/api/keys/{id}` | — | 204 |
| `GET` | `/api/keys/{id}/stats?days=7` | — | 키별 통계 |
| `GET` | `/api/keys/stats?days=7` | — | 전체 통계 |

#### 통계 응답 예시

```json
{
  "totalRequests": 1423,
  "requestsByDay": [
    { "date": "2026-03-05", "count": 210 },
    { "date": "2026-03-06", "count": 87 }
  ],
  "requestsByEndpoint": [
    { "path": "/v1/embeddings", "count": 800 },
    { "path": "/v1/chat/completions", "count": 623 }
  ],
  "errorRate": 0.02,
  "avgDurationMs": 145
}
```

### ApiKeyService 주요 메서드

```csharp
Task<(ApiKey entity, string fullKey)> CreateKeyAsync(string name)
Task<ApiKey?> ValidateKeyAsync(string token)
Task DeleteKeyAsync(Guid id)
Task<IReadOnlyList<ApiKey>> GetAllKeysAsync()
Task<ApiKeyStats> GetKeyStatsAsync(Guid id, int days)
Task<ApiKeyStats> GetGlobalStatsAsync(int days)
Task LogRequestAsync(Guid keyId, string path, string method, int status, long durationMs)
```

### 401 응답 형식 (기존 ErrorMiddleware 패턴 일치)

```json
{
  "error": {
    "message": "Invalid API key",
    "type": "auth_error",
    "code": "invalid_api_key"
  }
}
```

## UI

### 사이드바

`Layout.tsx` 하단 섹션에 `Key` 아이콘으로 `/api-keys` 항목 추가 (Models, API Docs 사이).

### `/api-keys` 페이지 구성

1. **상태 배너**
   - 키 없음: 노란 경고 — "현재 접근 제한 없음 — 키를 생성하면 인증이 활성화됩니다"
   - 키 있음: 초록 — "N개 API Key 활성 — 모든 요청에 Bearer 인증 필요"

2. **키 테이블**
   - 컬럼: Name | Prefix | Created | Last Used | Total Requests | 삭제 버튼

3. **Create Key 버튼 → 다이얼로그**
   - 이름 입력 → 생성 → 풀 키를 복사 버튼과 함께 표시 (다이얼로그 닫으면 재확인 불가 경고)

4. **통계 카드** (키 테이블 아래)
   - 기간 선택: 1일 / 7일 / 30일
   - 전체 요청 수, 일별 바 차트, 엔드포인트별 요청 수, 평균 응답시간, 에러율

## NuGet 의존성 추가

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
```

버전은 `Directory.Packages.props`에서 중앙 관리.

## 구현 순서

1. NuGet 패키지 추가 + EF Core 설정
2. 데이터 모델 + DbContext + 마이그레이션
3. ApiKeyService
4. ApiKeyMiddleware
5. ApiKeyEndpoints + Program.cs 등록
6. UI: `/api-keys` 페이지 + Layout 사이드바
7. Swagger에 Bearer 인증 문서 추가

## 비범위 (v1 제외)

- 키 만료일
- 도메인/IP 제한
- 키별 요청 속도 제한 (rate limiting)
- 키 이름 수정 (삭제 후 재생성으로 대체)
