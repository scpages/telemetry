namespace Integrator.Telemetry;

/// <summary>One entry in the event log — a telemetry pull or a game action trigger.</summary>
internal sealed record LogEntry(
    DateTime Timestamp,
    string Source,        // e.g. "POST /api/v1/action", "POST /api/v1/telemetry", "UI pull"
    string? RequestBody,  // request body (truncated); null if none
    string Status,        // OK | FILTERED | MAP_INACTIVE | FAILED | ERROR
    string? ResponseBody  // response body or error message (truncated); null if none
);
