// A reader's cache entry, which can hold nothing: an id the client has no art for is remembered, so its file is not
// read again.
#nullable enable

namespace Uoaix.Assist;

public sealed record Cached<T>(T? Value) where T : class;
