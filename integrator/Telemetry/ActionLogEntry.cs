namespace Integrator.Telemetry;

/// <summary>One entry in the event log — either a game action trigger or a telemetry pull.</summary>
internal sealed record LogEntry(
    DateTime Timestamp,
    string Kind,     // "action" | "telemetry"
    string Name,     // action name, or "pull"
    int HoldMs,      // action only; 0 otherwise
    string Status,   // OK | FILTERED | MAP_INACTIVE | FAILED | ERROR
    string? Detail   // null on success; error message when Status == ERROR
);
