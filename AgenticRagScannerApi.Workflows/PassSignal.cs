namespace AgenticRagScannerApi.Workflows;

/// <summary>
/// Signals that drive one topic group's loop executor. The workflow starts on <see cref="Start"/>;
/// the executor sends itself <see cref="Continue"/> (a self-edge) to run another pass.
/// </summary>
public enum PassSignal
{
    /// <summary>Begin the first pass.</summary>
    Start,

    /// <summary>Run another pass - the loop controller chose to keep looping.</summary>
    Continue,
}
