namespace Integrator.Telemetry;

internal sealed record ActionLogEntry(
    DateTime Timestamp,
    string ActionName,
    int HoldMs,
    string Status,   // OK | FILTERED | MAP_INACTIVE | FAILED | ERROR
    string? Detail   // null on success; error message when Status == ERROR
);
