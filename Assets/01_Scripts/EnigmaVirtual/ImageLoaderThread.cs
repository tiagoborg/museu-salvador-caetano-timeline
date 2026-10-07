//#define DO_LOGS // use this on classes to print logs.
using EnigmaVirtual.Queue;
using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// Turns downloaded <see cref="Texture"/>s into <see cref="Sprite"/>s, one at a time, in priority
    /// order. <b>It is not a thread and cannot become one</b>, despite the name.
    /// </summary>
    /// <remarks>
    /// <see cref="Sprite.Create"/> is a main-thread-only Unity call, so there is nothing here to move
    /// off the main thread - and it is cheap, since a sprite is a view onto a texture rather than a
    /// copy of it. What the queue buys is ordering and a single-item throttle, not concurrency. The
    /// name is kept only because it is authored in `CoreSetup.unity`, and renaming the class breaks
    /// that binding for every branched project; treat it as "image load queue".
    /// <para>
    /// A <c>SpriteCreateLayerJob</c> used to sit at the bottom of this file, scheduled by an
    /// <c>ExecuteSpriteCreateJob</c> that nothing called. It could not have worked: an
    /// <c>IJobParallelFor</c> struct cannot hold managed <see cref="UnityEngine.Object"/> references,
    /// its <c>Execute</c> called main-thread-only <see cref="Sprite.Create"/> from a worker, and the
    /// caller seeded it with <c>Sprite.Create(null, ...)</c>. Both are deleted rather than fixed -
    /// the job system is the wrong tool for a main-thread-only API.
    /// </para>
    /// </remarks>
    public class ImageLoaderThread : MonoBehaviour
    {
        private bool isLoading = false;
        private readonly uint maxActiveLoads = 1;
        private uint activeLoads = 0;
        private int queuePriority = 0; // current item queue priority

        private SimplePriorityQueue<ImageQueueItem> queue = new SimplePriorityQueue<ImageQueueItem>(); // Item load queue

        /// <summary>
        /// Requests media load.
        /// </summary>
        /// <param name="item">Media to be dowloaded.</param>
        /// <param name="priority">Media load priority (0 the highest priority).</param>
        public void Enqueue(ImageQueueItem item, int priority = -1)
        {
            Development.Log($"Enqueue ImageQueueItem []");

            if (queue.Count == 0 && !isLoading)
                queuePriority = 0;

            // The return value used to be discarded. A rejected duplicate never reaches LoadImage, so
            // its callback never fires and the DownloadableImage that queued it stays _isLoading = true
            // forever - silent. Unreachable while the drain is synchronous (the queue holds one item),
            // but it is the shape that bites the moment anything defers.
            if (!queue.EnqueueWithoutDuplicates(item, priority >= 0 ? priority : queuePriority++))
                Development.LogWarning("ImageQueueItem rejected as a duplicate; its callback will not "
                    + "fire. The already-queued copy carries the same callback, so this is only a "
                    + "problem if the caller is waiting on its own invocation.");

            TryDequeue();
        }

        /// <summary>
        /// Attempts to remove an item from the queue. The item does not need to be the head of the queue.  
        /// </summary>
        public bool Remove(ImageQueueItem item) => queue.TryRemove(item);

        /// <summary>
        /// Call this method to change the priority of an item.
        /// /// Returns true if the item priority was updated, false otherwise.
        /// </summary>
        /// <param name="item">Item priority to be updated.</param>
        /// <param name="priority">New load priority to assign to item.</param>
        /// <returns></returns>
        public bool UpdatePriority(ImageQueueItem item, int priority) => queue.TryUpdatePriority(item, priority);

        /// <summary>
        /// Drains the queue, highest priority first.
        /// </summary>
        /// <remarks>
        /// <b>This used to recurse, one stack frame per queued item.</b> <c>LoadImage</c> was a
        /// coroutine whose only <c>yield</c> sat *after* its own <c>TryDequeue()</c> call, and
        /// <c>StartCoroutine</c> runs a coroutine body synchronously up to its first yield - so the
        /// whole chain ran inside the original call and each item nested a level deeper. Queue depth
        /// was stack depth. A loop does identical work, flat.
        /// <para>
        /// The <c>activeLoads</c> gate stays, and it is not vestigial: it is the re-entrancy guard. A
        /// callback that enqueues another image lands here while the loop is still running, and this
        /// early return hands the new item to the loop already draining rather than starting a second.
        /// </para>
        /// </remarks>
        private void TryDequeue()
        {
            Development.Log($"TryDequeue with queue size {queue.Count} and {activeLoads}/{maxActiveLoads} active loads");

            if (activeLoads >= maxActiveLoads)
                return;

            while (queue.Count > 0)
            {
                activeLoads++;
                isLoading = true;

                // finally, because the callback fans out through DownloadableImage into
                // onLoadCompleteEvent - arbitrary project code. A throw there used to skip the
                // decrement, pin activeLoads at 1, and send every later TryDequeue straight out of the
                // guard above: sprite loading dead for the process lifetime, app-wide, since this
                // component is a singleton on Core. One exception, then silence.
                try
                {
                    LoadImage(queue.Dequeue());
                }
                finally
                {
                    activeLoads--;
                }
            }

            isLoading = false;
        }

        /// <summary>
        /// Creates the sprite and hands it to the item's callback. <b>The callback takes ownership</b> -
        /// <see cref="Sprite.Create"/> allocates a native object, and this class never sees it again.
        /// </summary>
        /// <remarks>
        /// Synchronous, and it always was: this was an <c>IEnumerator</c> whose only <c>yield</c> was
        /// the last statement, so it never deferred anything by even one frame. Made a plain method so
        /// the shape matches the behaviour. <see cref="Sprite.Create"/> is main-thread-only, so this
        /// cannot be moved off it - see the class remarks about the name.
        /// </remarks>
        private void LoadImage(ImageQueueItem item)
        {
            if (item.texture == null)
            {
                Development.LogError("ImageQueueItem has a null texture; no sprite to create.");
                item.outMethod?.Invoke(null);
                return;
            }

            Sprite outSprite = Sprite.Create(item.texture as Texture2D,
                new Rect(0, 0, item.texture.width, item.texture.height), new Vector2(0.5f, 0.5f));

            item.outMethod?.Invoke(outSprite);
        }
    }

    public struct ImageQueueItem
    {
        public Texture texture { get; private set; }
        public System.Action<Sprite> outMethod;

        public ImageQueueItem(Texture texture, System.Action<Sprite> outMethod)
        {
            this.texture = texture;
            this.outMethod = outMethod;
        }
    }
}