using System;
using UnityEngine.Networking;

/// <summary>
/// One place where a configured timeout becomes a <see cref="UnityWebRequest.timeout"/>, and the two
/// policies this framework applies when it does.
/// </summary>
/// <remarks>
/// The numbers live here rather than at each call site because they now have a consumer that is not a
/// call site at all: <c>Core.BootWatchdog_IE</c> sizes its stall budget off the timeout a download can
/// actually take, so it needs the *resolved* value without issuing a request. Two call sites already
/// shared the identical ceiling, which would have made three copies of one number - the exact shape of
/// the defect this class was written to remove.
/// </remarks>
internal static class WebRequestExtensions
{
    /// <summary>
    /// Ceiling for a JSON call. Low on purpose: a server that has not answered in five minutes is
    /// not going to.
    /// </summary>
    internal const int ApiCeilingSeconds = 300;

    /// <summary>
    /// What an unconfigured <c>Api.timeout</c> resolves to. On that block <c>0</c> means "unset", so
    /// it lands on the field's own initializer rather than on the ceiling.
    /// </summary>
    internal const int ApiUnconfiguredSeconds = 30;

    /// <summary>
    /// Ceiling for a bulk file transfer - an asset download or a texture fetch. Twelve times
    /// <see cref="ApiCeilingSeconds"/>, and the difference is load-bearing.
    /// </summary>
    /// <remarks>
    /// <c>www.timeout</c> bounds the <i>whole</i> request, and a failed attempt deletes its
    /// <c>.part</c> file, so nothing resumes - every retry restarts at byte zero. A ceiling below the
    /// honest transfer time therefore does not slow a large asset down, it makes it permanently
    /// undownloadable, and the only trace is one "Giving up" line. Pick a ceiling above the worst
    /// legitimate transfer, not a comfortable one.
    ///
    /// <para>It doubles as the value a configured <c>0</c> resolves to, because on these blocks
    /// <c>0</c> has always meant "no limit" - so the nearest bounded reading of it is the largest
    /// legal wait, not the smallest. Resolving it to 30 instead would cut a transfer off sooner than
    /// the shipped 60 does, on exactly the requests that cannot resume.</para>
    /// </remarks>
    internal const int FileTransferCeilingSeconds = 3600;

    /// <summary>
    /// Resolves an operator's configured timeout to a value that cannot mean "never fail".
    /// </summary>
    /// <remarks>
    /// Two values are refused. <c>0</c>, because UnityWebRequest reads it as <b>wait forever</b>;
    /// and anything above <paramref name="maxSeconds"/>, which is the same outcome reached by
    /// typing an extra digit.
    ///
    /// <para><paramref name="configuredSeconds"/> is <c>long</c> so both an <c>int</c> and a
    /// <c>uint</c> settings field convert implicitly, and the ceiling is applied before anything is
    /// narrowed. That ordering matters: <c>(int)uint.MaxValue</c> is <c>-1</c>, and UnityWebRequest
    /// stores a negative timeout as <b>0</b> - measured - so an over-large value used to land on
    /// exactly the unbounded wait it looked like it was avoiding.</para>
    ///
    /// <para>Prefer <see cref="EffectiveApiTimeout"/> or <see cref="EffectiveFileTransferTimeout"/>,
    /// which name a policy. This overload exists for them and for tests.</para>
    /// </remarks>
    /// <param name="configuredSeconds">The operator's value. 0 or less means "unconfigured".</param>
    /// <param name="maxSeconds">
    /// Ceiling for this kind of request, and it must be positive - <c>Math.Min(configured, 0)</c>
    /// would assign 0 and restore the unbounded wait this method exists to prevent. A non-positive
    /// ceiling falls back to <paramref name="unconfiguredSeconds"/>, which means it <b>discards a
    /// perfectly good configured value</b>: measured, a ceiling of 0 turns a configured 120 into 30.
    /// That is the safe direction of a wrong answer rather than a right one, and it is unreachable
    /// while every caller passes one of the constants above - which is the actual guarantee.
    /// </param>
    /// <param name="unconfiguredSeconds">What a configured 0 resolves to. Must be positive.</param>
    internal static int EffectiveTimeout(long configuredSeconds, int maxSeconds, int unconfiguredSeconds)
    {
        int ceiling = maxSeconds > 0 ? maxSeconds : unconfiguredSeconds;

        return configuredSeconds > 0
            ? (int)Math.Min(configuredSeconds, ceiling)
            : unconfiguredSeconds;
    }

    /// <summary>Resolves <c>Api.timeout</c>: 0 means "unset" and becomes 30.</summary>
    internal static int EffectiveApiTimeout(long configuredSeconds)
    {
        return EffectiveTimeout(configuredSeconds, ApiCeilingSeconds, ApiUnconfiguredSeconds);
    }

    /// <summary>
    /// Resolves an <c>AssetDownload</c> or <c>TextureCache</c> timeout: 0 means "no limit" and
    /// becomes <see cref="FileTransferCeilingSeconds"/>.
    /// </summary>
    internal static int EffectiveFileTransferTimeout(long configuredSeconds)
    {
        return EffectiveTimeout(configuredSeconds, FileTransferCeilingSeconds,
            FileTransferCeilingSeconds);
    }

    /// <summary>Applies the <see cref="EffectiveApiTimeout"/> policy to a request.</summary>
    internal static void SetApiTimeout(this UnityWebRequest www, long configuredSeconds)
    {
        www.timeout = EffectiveApiTimeout(configuredSeconds);
    }

    /// <summary>Applies the <see cref="EffectiveFileTransferTimeout"/> policy to a request.</summary>
    internal static void SetFileTransferTimeout(this UnityWebRequest www, long configuredSeconds)
    {
        www.timeout = EffectiveFileTransferTimeout(configuredSeconds);
    }
}
