using EnigmaVirtual;
using EnigmaVirtual.Components.Video;

public class VideoPlayerController : VideoPlayerControllerBase
{
    public override string url
    {
        get { return videoPlayer.url; }
        set
        {
            _url = Core.settings.AppData.GetAssetPath(value);
            videoPlayer.url = _url;
        }
    }

    // Instance methods, not just the extensions in App/Extensions/MediaVOExtensions.cs, and the reason
    // is a failure mode a compile error would never surface. A UnityEvent persistent call is resolved by
    // reflection over INSTANCE methods, so a `UnityEvent<MediaVO>` wired in the Inspector to
    // Play(MediaVO) - which is exactly what ImageGalleryItem.onMediaClickEvent is for - would have found
    // nothing once that overload moved out of the framework, and UnityEvent skips a call it cannot
    // resolve WITHOUT logging. A gallery click would have stopped playing video, silently, in any
    // branched project that had wired it. These also restore the override point that Play(MediaVO) had
    // as a virtual member, since extension methods cannot be overridden.
    public void Play(MediaVO value) => MediaVOExtensions.Play(this, value);

    public void Prepare(MediaVO value, bool playOnPrepared = false)
        => MediaVOExtensions.Prepare(this, value, playOnPrepared);
}
