using GO.Log.Abstractions.Models;

namespace GO.Log.Models;

internal sealed record QueuedLogEntry(long Sequence, GOLogEntry Entry);
