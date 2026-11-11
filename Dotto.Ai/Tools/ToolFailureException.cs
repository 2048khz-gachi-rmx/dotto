namespace Dotto.Ai.Tools;

/// <summary>
/// Raised by a tool when the call did not succeed for a reason the model should see
/// (download failed, sandbox rejected the request, file missing). The turn wrapper
/// records it as a failed call and returns the message as the tool result, so the
/// model gets the same text it used to get from a returned error string.
/// </summary>
public sealed class ToolFailureException(string message) : Exception(message);
