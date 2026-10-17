using mGO.Log.Abstractions.Models;

namespace mGO.Log.Models;

internal sealed record QueuedLogEntry(long Sequence, GOLogEntry Entry);
