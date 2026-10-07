using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// A console to display Unity's debug logs in-game.
    ///
    /// Version: 1.2.2 - Fixed memory leaks (Texture2D, GUIStyle, LINQ per-frame allocations)
    /// </summary>
    public class Console : MonoBehaviour
    {
        #region Inspector Settings

        /// <summary>
        /// The hotkey to show and hide the console window.
        /// </summary>
        public KeyCode toggleKey = KeyCode.BackQuote;

        /// <summary>
        /// Whether to open as soon as the game starts.
        /// </summary>
        public bool openOnStart = false;

        /// <summary>
        /// Whether to open the window by shaking the device (mobile-only).
        /// </summary>
        public bool shakeToOpen = false;

        private bool onlyLastMessage = false;

        /// <summary>
        /// Also require touches while shaking to avoid accidental shakes.
        /// </summary>
        public bool shakeRequiresTouch = false;

        /// <summary>
        /// The (squared) acceleration above which the window should open.
        /// </summary>
        public float shakeAcceleration = 3f;

        /// <summary>
        /// The number of seconds that have to pass between visibility toggles.
        /// </summary>
        public float toggleThresholdSeconds = .5f;
        private float lastToggleTime;

        /// <summary>
        /// Whether to only keep a certain number of logs.
        /// </summary>
        public bool restrictLogCount = true;

        /// <summary>
        /// Number of logs to keep before removing old ones.
        /// </summary>
        public int maxLogCount = 1000;

        /// <summary>
        /// Font size to display log entries with.
        /// </summary>
        public int logFontSize = 20;

        /// <summary>
        /// Amount to scale UI by.
        /// </summary>
        public float scaleFactor = .95f;

        #endregion

        static readonly GUIContent clearLabel     = new GUIContent("Clear", "Clear the contents of the console.");
        static readonly GUIContent onlyOne        = new GUIContent("Show Only Last Message", "Show only last messages.");
        static readonly GUIContent collapseLabel  = new GUIContent("Collapse", "Hide repeated messages.");

        const int    margin      = 20;
        const string windowTitle = "Console";

        static readonly Dictionary<LogType, Color> logTypeColors = new Dictionary<LogType, Color>
        {
            { LogType.Assert,    Color.white  },
            { LogType.Error,     Color.red    },
            { LogType.Exception, Color.red    },
            { LogType.Log,       Color.white  },
            { LogType.Warning,   Color.yellow },
        };

        bool isCollapsed;
        bool isVisible;

        readonly List<Log> logs = new List<Log>();
        readonly ConcurrentQueue<Log> queuedLogs = new ConcurrentQueue<Log>();

        // Filtered log cache — rebuilt only when logs change, not every frame.
        readonly List<Log> filteredLogs = new List<Log>();
        bool filterDirty = true;

        Vector2 scrollPosition;
        float windowX = margin;
        float windowY = margin;
        Rect newWindowRect;

        readonly Dictionary<LogType, bool> logTypeFilters = new Dictionary<LogType, bool>
        {
            { LogType.Assert,    true },
            { LogType.Error,     true },
            { LogType.Exception, true },
            { LogType.Log,       true },
            { LogType.Warning,   true },
        };

        // ── Cached textures (created once, destroyed in OnDestroy) ──────────────
        Texture2D _windowBgTexture;
        Texture2D _titleBgTexture;
        Texture2D _rowEvenTexture;
        Texture2D _rowOddTexture;

        // ── Cached styles (created once, rebuilt if font size changes) ──────────
        GUIStyle _windowStyle;
        GUIStyle _logStyle;
        GUIStyle _logStyleEven;
        GUIStyle _logStyleOdd;
        GUIStyle _badgeStyle;
        GUIStyle _buttonStyle;
        GUIStyle _toggleStyle;
        GUIStyle _titleStyle;

        /// <summary>
        /// Ceiling on how many times one coalesced message is drawn in the expanded view.
        /// <para>
        /// This has to be bounded. <c>ProcessLogItem</c> folds a message identical to the previous
        /// one by incrementing its <c>count</c>, and <c>TrimExcessLogs</c> caps the number of
        /// <em>entries</em> — so nothing caps <c>count</c>, and a message repeating every frame
        /// would otherwise be redrawn that many times on every layout and repaint pass.
        /// </para>
        /// A const rather than a serialized field: it bounds a draw cost, not anything an operator
        /// tunes, and the true total is always reported beside the truncated rows.
        /// </summary>
        const int maxRepeatDraw = 50;

        /// <summary>Cached so the toolbar does not allocate a fresh array on every layout pass.</summary>
        static readonly LogType[] _logTypes = (LogType[])Enum.GetValues(typeof(LogType));

        // Hoisted: GUILayout.ExpandWidth/Width allocate an option plus a params array per call, and
        // these two sit on the per-row path, evaluated on both the layout and the repaint pass.
        static readonly GUILayoutOption[] _expandWidth = { GUILayout.ExpandWidth(true) };
        static readonly GUILayoutOption[] _badgeWidth  = { GUILayout.Width(50) };

        // ── Virtualisation state ────────────────────────────────────────────────
        // Cumulative pixel offsets, one per display entry plus a terminator, so _cumHeights[i] is
        // the top of entry i and the last element is the total content height. Rebuilt only when
        // something that changes layout changes - never per frame.
        readonly List<float> _cumHeights = new List<float>();
        readonly GUIContent  _measure    = new GUIContent();

        bool  _heightsDirty     = true;
        float _heightsWidth     = -1f;
        int   _heightsFontSize  = -1;
        bool  _heightsCollapsed;

        // Geometry from the previous frame. IMGUI cannot tell us the viewport before laying the
        // content out, and one frame of lag self-corrects; it only ever means we draw a few rows
        // more or fewer than strictly needed.
        float _lastContentWidth;
        Rect  _lastOuterRect;

        // ── Drag-to-scroll ──────────────────────────────────────────────────────
        // Press anywhere on the list and drag. Touch reaches IMGUI as mouse events, so this is the
        // same code path on a kiosk panel as with a mouse.
        bool  _isDragging;
        float _dragStartMouseY;
        float _dragStartScroll;

        /// <summary>
        /// The scroll offset as <c>BeginScrollView</c> last resolved it, which is not the same thing
        /// as <see cref="scrollPosition"/>. While the view is following new messages,
        /// <see cref="ScrollToBottom"/> parks <c>int.MaxValue</c> in that field for IMGUI to clamp,
        /// so reading it at press time starts a drag from two billion and every plausible drag still
        /// lands past the end — the list does not move. A drag must begin from the resolved offset.
        /// </summary>
        float _clampedScrollY;

        /// <summary>Width reserved for the vertical scrollbar; the drag area stops short of it.</summary>
        const int scrollbarWidth = 25;

        // Resolved once per frame, on the Layout pass, and reused by every other pass. The two
        // passes MUST emit the same controls, and scrollPosition can move between them.
        int _visFirst;
        int _visLast;

        static readonly GUIContent repeatOverflowLabel =
            new GUIContent("  ⋯ repeated, total:", "Older repeats are not drawn; this is the true count.");
        int _cachedFontSize = -1;

        #region MonoBehaviour Messages

        void Awake()
        {
            InitTextures();
        }

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += HandleLogThreaded;
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleLogThreaded;
        }

        void OnDestroy()
        {
            // Destroy all native Texture2D objects to prevent memory leaks.
            DestroyTexture(ref _windowBgTexture);
            DestroyTexture(ref _titleBgTexture);
            DestroyTexture(ref _rowEvenTexture);
            DestroyTexture(ref _rowOddTexture);
        }

        void Start()
        {
            if (openOnStart)
            {
                SetVisible(true);
            }
        }

        /// <summary>
        /// Opening always lands on the newest message. Without this the view opens at scroll 0 —
        /// the oldest entry — and, because following only resumes once you are already at the end,
        /// it would never catch up on its own.
        /// </summary>
        void SetVisible(bool visible)
        {
            isVisible = visible;

            if (visible) ScrollToBottom();
        }

        void Update()
        {
            UpdateQueuedLogs();

            float curTime = Time.realtimeSinceStartup;

            if (Input.GetKeyDown(toggleKey))
            {
                SetVisible(!isVisible);
            }

            if (shakeToOpen &&
                Input.acceleration.sqrMagnitude > shakeAcceleration &&
                curTime - lastToggleTime >= toggleThresholdSeconds &&
                (!shakeRequiresTouch || Input.touchCount > 2))
            {
                SetVisible(!isVisible);
                lastToggleTime = curTime;
            }
        }

        void OnGUI()
        {
            if (!isVisible) return;

            // Rebuild styles only when font size changes, not every frame.
            EnsureStyles();

            // Clamped because it divides below and a serialized 0 would make width and height
            // Infinity, which reaches GUILayout as an infinite Rect. Clamp rather than log: this
            // runs every frame, and a [Range] would not touch a value a scene already carries.
            float scale = Mathf.Max(scaleFactor, .01f);

            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);

            // GUI.matrix scales GUI units down to pixels by scale, so a window that spans the
            // screen must be Screen.width / scale units wide. Multiplying instead asks for a
            // window narrower than the screen, and the margin must come off in GUI units at every
            // scale, not only at 1.
            float width  = (Screen.width  / scale) - (margin * 2);
            float height = (Screen.height / scale) - (margin * 2);
            Rect windowRect = new Rect(windowX, windowY, width, height);

            newWindowRect = GUILayout.Window(123456, windowRect, DrawWindow, "", _windowStyle);
            windowX = newWindowRect.x;
            windowY = newWindowRect.y;
        }

        #endregion

        #region Initialisation

        void InitTextures()
        {
            _windowBgTexture = MakeSolidTexture(Color.gray);
            _titleBgTexture  = MakeSolidTexture(("#313131").ToUnityColor());
            _rowEvenTexture  = MakeSolidTexture(Color.gray);
            _rowOddTexture   = MakeSolidTexture(("#474747FF").ToUnityColor());
        }

        /// <summary>
        /// (Re)builds GUIStyles. Called once and again only if logFontSize changes at runtime.
        /// </summary>
        void EnsureStyles()
        {
            if (_cachedFontSize == logFontSize && _windowStyle != null) return;

            _cachedFontSize = logFontSize;

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _windowBgTexture;

            _logStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = logFontSize
            };

            // Two prebuilt row styles instead of writing _logStyle.normal.background per row:
            // that setter crosses into native code and ran twice per row per frame.
            _logStyleEven = new GUIStyle(_logStyle);
            _logStyleOdd  = new GUIStyle(_logStyle);
            _logStyleEven.normal.background = _rowEvenTexture;
            _logStyleOdd.normal.background  = _rowOddTexture;

            _badgeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = logFontSize
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = logFontSize
            };
            _buttonStyle.normal.textColor = Color.white;

            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = logFontSize
            };
            _toggleStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle
            {
                fontSize   = logFontSize,
                fontStyle  = FontStyle.Bold,
                alignment  = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor      = Color.white;
            _titleStyle.normal.background     = _titleBgTexture;
        }

        static Texture2D MakeSolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null)
            {
                UnityEngine.Object.Destroy(tex);
                tex = null;
            }
        }

        #endregion

        #region Drawing

        void DrawLog(Log log, GUIStyle rowStyle)
        {
            GUI.contentColor = logTypeColors[log.type];

            if (isCollapsed)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(log.GetTruncatedMessage(), rowStyle, _expandWidth);
                GUILayout.Label(log.CountText, _badgeStyle, _badgeWidth);
                GUILayout.EndHorizontal();
            }
            else
            {
                var shown = Mathf.Min(log.count, maxRepeatDraw);

                for (var i = 0; i < shown; i++)
                {
                    GUILayout.Label(log.GetTruncatedMessage(), rowStyle);
                }

                // Report the truncation rather than silently drawing fewer rows than happened —
                // a cap you cannot see reads exactly like a message that stopped repeating.
                if (log.count > shown)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(repeatOverflowLabel, rowStyle, _expandWidth);
                    GUILayout.Label(log.CountText, _badgeStyle, _badgeWidth);
                    GUILayout.EndHorizontal();
                }
            }

            GUI.contentColor = Color.white;
        }

        /// <summary>
        /// Number of rows <see cref="DrawLog"/> emits for one entry.
        /// </summary>
        int RowsFor(Log log)
        {
            if (isCollapsed) return 1;

            var shown = Mathf.Min(log.count, maxRepeatDraw);
            return shown + (log.count > shown ? 1 : 0);
        }

        /// <summary>
        /// Measures every display entry once and stores cumulative offsets, so the draw path can
        /// binary-search the visible range instead of laying out all of them.
        /// <para>
        /// This is O(n) and runs only when something that changes layout changes — the filtered set,
        /// the font size, the collapse mode, or the content width. The draw path that consumes it
        /// runs every frame and is O(log n + visible).
        /// </para>
        /// </summary>
        void RebuildHeightsIfNeeded(List<Log> displayLogs, float width)
        {
            if (width <= 0f) return;

            if (!_heightsDirty
                && _heightsFontSize == logFontSize
                && _heightsCollapsed == isCollapsed
                && Mathf.Approximately(_heightsWidth, width)
                && _cumHeights.Count == displayLogs.Count + 1)
            {
                return;
            }

            _heightsDirty     = false;
            _heightsWidth     = width;
            _heightsFontSize  = logFontSize;
            _heightsCollapsed = isCollapsed;

            // A row that carries a count badge gives the label less width and is at least as tall
            // as the badge itself.
            var badgedWidth = Mathf.Max(1f, width - 50f);
            var badgeHeight = _badgeStyle.CalcHeight(_measure, 50f);

            _measure.text = repeatOverflowLabel.text;
            var overflowHeight = Mathf.Max(_logStyleEven.CalcHeight(_measure, badgedWidth), badgeHeight);

            _cumHeights.Clear();
            _cumHeights.Add(0f);

            var running = 0f;
            for (int i = 0; i < displayLogs.Count; i++)
            {
                var log = displayLogs[i];
                _measure.text = log.GetTruncatedMessage() ?? string.Empty;

                if (isCollapsed)
                {
                    running += Mathf.Max(_logStyleEven.CalcHeight(_measure, badgedWidth), badgeHeight);
                }
                else
                {
                    var unit  = _logStyleEven.CalcHeight(_measure, width);
                    var shown = Mathf.Min(log.count, maxRepeatDraw);

                    running += unit * shown;
                    if (log.count > shown) running += overflowHeight;
                }

                _cumHeights.Add(running);
            }
        }

        /// <summary>
        /// First and last+1 display index intersecting the viewport, from the cumulative offsets.
        /// Resolved on the Layout pass only — Layout and Repaint must emit identical controls, and
        /// <c>scrollPosition</c> can move between them.
        /// </summary>
        void ResolveVisibleRange(int count, float scrollY, float viewportHeight)
        {
            if (count == 0 || _cumHeights.Count != count + 1 || viewportHeight <= 0f)
            {
                _visFirst = 0;
                _visLast  = count;
                return;
            }

            // Whole content fits: nothing to virtualise, and drawing it all keeps the small-list
            // case byte-identical to the unvirtualised path.
            if (_cumHeights[count] <= viewportHeight)
            {
                _visFirst = 0;
                _visLast  = count;
                return;
            }

            // ScrollToBottom parks a sentinel here until BeginScrollView clamps it, so clamp before
            // searching or that frame resolves to an empty range and draws nothing.
            var top    = Mathf.Clamp(scrollY, 0f, Mathf.Max(0f, _cumHeights[count] - viewportHeight));
            var bottom = top + viewportHeight;

            _visFirst = UpperBound(top) - 1;
            if (_visFirst < 0) _visFirst = 0;

            _visLast = UpperBound(bottom);
            if (_visLast > count) _visLast = count;
            if (_visLast < _visFirst) _visLast = _visFirst;
        }

        /// <summary>Index of the first cumulative offset strictly greater than <paramref name="y"/>.</summary>
        int UpperBound(float y)
        {
            int lo = 0, hi = _cumHeights.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cumHeights[mid] > y) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }

        /// <summary>
        /// Press-and-drag anywhere on the log list to scroll it, the way a touch panel expects.
        /// <para>
        /// Uses the previous frame's scroll-view rect, because IMGUI cannot report it before the
        /// content is laid out. One frame of lag is invisible for a press, and the rect only changes
        /// when the window is resized.
        /// </para>
        /// The content follows the pointer — dragging down reveals earlier messages — and the
        /// scrollbar band is excluded so IMGUI keeps handling thumb drags itself. Touch arrives as
        /// mouse events, so this is one code path for both.
        /// <para>
        /// Takes the <see cref="Event"/> rather than reading <c>Event.current</c>, so it can be
        /// driven from a test. <c>Event.current</c> is bound to a live IMGUI pass and reads back null
        /// anywhere else, and this project builds against the legacy Input Manager, so there is no
        /// injection route that would reach a handler which read it directly.
        /// </para>
        /// </summary>
        void HandleDragScroll(Event e)
        {
            // Nothing to hit-test against until the list has been drawn at least once.
            if (e == null || _lastOuterRect.width <= 0f || _lastOuterRect.height <= 0f) return;

            var dragArea = _lastOuterRect;
            dragArea.width = Mathf.Max(0f, dragArea.width - scrollbarWidth);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !dragArea.Contains(e.mousePosition)) break;

                    _isDragging      = true;
                    _dragStartMouseY = e.mousePosition.y;
                    _dragStartScroll = _clampedScrollY;

                    // Deliberately not consumed: a press alone should still reach anything else
                    // that wants it, and nothing in the list acts on a click.
                    break;

                case EventType.MouseDrag:
                    if (!_isDragging) break;

                    // Measured from where the press started rather than accumulated per event, so
                    // the list stays under the pointer even after it has been dragged past an end.
                    scrollPosition.y = _dragStartScroll - (e.mousePosition.y - _dragStartMouseY);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (!_isDragging) break;

                    _isDragging = false;
                    e.Use();
                    break;
            }
        }

        void DrawLogList()
        {
            // Widen scrollbar for touch usability.
            GUI.skin.verticalScrollbar.fixedWidth          = scrollbarWidth;
            GUI.skin.verticalScrollbarThumb.fixedWidth     = scrollbarWidth;
            GUI.skin.verticalScrollbarUpButton.fixedWidth  = scrollbarWidth;
            GUI.skin.verticalScrollbarDownButton.fixedWidth = scrollbarWidth;

            HandleDragScroll(Event.current);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.Space(logFontSize * 1.5f);
            GUILayout.BeginVertical();

            // Use pre-filtered cache; rebuild only when dirty.
            RebuildFilteredLogsIfNeeded();

            var displayLogs = onlyLastMessage
                ? (filteredLogs.Count > 0 ? filteredLogs.GetRange(filteredLogs.Count - 1, 1) : _emptyLogList)
                : filteredLogs;

            RebuildHeightsIfNeeded(displayLogs, _lastContentWidth);

            if (Event.current.type == EventType.Layout)
            {
                ResolveVisibleRange(displayLogs.Count, scrollPosition.y, _lastOuterRect.height);
            }

            var first = Mathf.Clamp(_visFirst, 0, displayLogs.Count);
            var last  = Mathf.Clamp(_visLast,  first, displayLogs.Count);

            var haveOffsets = _cumHeights.Count == displayLogs.Count + 1;

            // The two spacers stand in for the rows that are not drawn, so the content keeps its
            // true height. That is what preserves the scrollbar's proportions and lets
            // IsScrolledToBottom keep working against a real inner rect.
            if (haveOffsets && first > 0) GUILayout.Space(_cumHeights[first]);

            for (int i = first; i < last; i++)
            {
                DrawLog(displayLogs[i], MathExtensions.IsOdd(i) ? _logStyleOdd : _logStyleEven);
            }

            if (haveOffsets && last < displayLogs.Count)
            {
                GUILayout.Space(_cumHeights[displayLogs.Count] - _cumHeights[last]);
            }

            GUILayout.EndVertical();
            var innerScrollRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndScrollView();
            var outerScrollRect = GUILayoutUtility.GetLastRect();

            // Feed this frame's geometry forward. Measured on Repaint, when the rects are real.
            if (Event.current.type == EventType.Repaint)
            {
                if (innerScrollRect.width > 0f)  _lastContentWidth = innerScrollRect.width;
                if (outerScrollRect.height > 0f) _lastOuterRect     = outerScrollRect;

                // Captured here, before the tail-follow below can park its sentinel in
                // scrollPosition: this is the offset IMGUI actually resolved for this frame.
                _clampedScrollY = scrollPosition.y;
            }

            if (!_isDragging
                && Event.current.type == EventType.Repaint
                && IsScrolledToBottom(innerScrollRect, outerScrollRect))
            {
                ScrollToBottom();
            }
        }

        static readonly List<Log> _emptyLogList = new List<Log>();

        /// <remarks>
        /// Spacing here must be flexible. Every control on this row is <c>ExpandWidth(false)</c>, so
        /// the row has a hard minimum width; fixed spacers add to it. GUILayout.Window sizes to the
        /// larger of the rect it is given and its content's minimum, so a row wider than the screen
        /// silently widens the window past the screen edge instead of wrapping or clipping — which
        /// is what happens in portrait, where there is far less width to spend.
        /// <para>
        /// The row's minimum scales with <see cref="logFontSize"/>, so that field has a ceiling too:
        /// at 1080x1920 with the default scale the controls need about 800 GUI units of the 1096
        /// available at size 20, and past roughly 26 they exceed it and the window starts
        /// overflowing again by this same route.
        /// </para>
        /// </remarks>
        void DrawToolbar()
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(clearLabel, _buttonStyle))
            {
                logs.Clear();
                filterDirty = true;
            }

            GUILayout.FlexibleSpace();

            foreach (var logType in _logTypes)
            {
                var prev = logTypeFilters[logType];
                logTypeFilters[logType] = GUILayout.Toggle(prev, logType.ToString(), _toggleStyle, GUILayout.ExpandWidth(false));
                if (logTypeFilters[logType] != prev) filterDirty = true;
                GUILayout.FlexibleSpace();
            }

            var prevCollapsed = isCollapsed;
            isCollapsed = GUILayout.Toggle(isCollapsed, collapseLabel, _toggleStyle, GUILayout.ExpandWidth(false));
            if (isCollapsed != prevCollapsed) filterDirty = true;

            GUILayout.FlexibleSpace();
            onlyLastMessage = GUILayout.Toggle(onlyLastMessage, onlyOne, _toggleStyle, GUILayout.ExpandWidth(false));
            GUILayout.Space(20);

            GUILayout.EndHorizontal();
        }

        void DrawWindow(int windowID)
        {
            DrawLogList();
            DrawToolbar();

            Rect titleBarRect = new Rect(0, 0, newWindowRect.width, logFontSize * 2);
            GUI.Box(titleBarRect, GUIContent.none);
            GUI.Label(titleBarRect, windowTitle, _titleStyle);

            GUI.DragWindow(titleBarRect);
        }

        #endregion

        #region Log Management

        /// <summary>
        /// Rebuilds the filtered log cache only when the source data or filters have changed.
        /// Avoids per-frame LINQ allocations.
        /// </summary>
        void RebuildFilteredLogsIfNeeded()
        {
            if (!filterDirty) return;

            filteredLogs.Clear();
            foreach (var log in logs)
            {
                if (logTypeFilters[log.type])
                    filteredLogs.Add(log);
            }

            filterDirty    = false;
            _heightsDirty  = true;
        }

        Log? GetLastLog()
        {
            return logs.Count == 0 ? (Log?)null : logs[logs.Count - 1];
        }

        void UpdateQueuedLogs()
        {
            Log log;
            while (queuedLogs.TryDequeue(out log))
            {
                ProcessLogItem(log);
            }
        }

        void HandleLogThreaded(string message, string stackTrace, LogType type)
        {
            queuedLogs.Enqueue(new Log
            {
                count      = 1,
                countText  = "1",
                message    = message,
                stackTrace = stackTrace,
                type       = type,
            });
        }

        void ProcessLogItem(Log log)
        {
            var lastLog = GetLastLog();
            var isDuplicateOfLastLog = lastLog.HasValue && log.Equals(lastLog.Value);

            if (isDuplicateOfLastLog)
            {
                log.count     = lastLog.Value.count + 1;
                log.countText = log.count.ToString();
                logs[logs.Count - 1] = log;
            }
            else
            {
                logs.Add(log);
                TrimExcessLogs();
            }

            filterDirty = true;
        }

        void TrimExcessLogs()
        {
            if (!restrictLogCount) return;

            var amountToRemove = logs.Count - maxLogCount;
            if (amountToRemove <= 0) return;

            logs.RemoveRange(0, amountToRemove);
        }

        #endregion

        #region Scroll Helpers

        /// <summary>
        /// Whether the view is close enough to the end that it should keep following new messages.
        /// <para>
        /// Deliberately a tolerance and not an equality. This used to ask
        /// <c>Mathf.Approximately(innerH, scrollPosition.y + outerH)</c>, whose epsilon at a content
        /// height of a few thousand pixels is about six thousandths of a pixel — and the two sides
        /// are derived differently, one of them net of the box padding, so they never agreed and the
        /// console never followed the tail.
        /// </para>
        /// The band is one row, so scrolling up by a line detaches and the view stays where it was
        /// put, while ordinary float drift at the end does not.
        /// </summary>
        bool IsScrolledToBottom(Rect innerScrollRect, Rect outerScrollRect)
        {
            var maxScroll = innerScrollRect.height - outerScrollRect.height;

            // Content shorter than the viewport: there is nowhere to scroll, so it is always at the end.
            if (maxScroll <= 0f) return true;

            return scrollPosition.y >= maxScroll - (logFontSize * 1.5f);
        }

        /// <summary>
        /// Jumps to the newest message.
        /// <para>
        /// The sentinel is deliberate: only <c>BeginScrollView</c> knows the exact maximum, because
        /// the outer rect includes a border that the visible viewport does not. Computing
        /// <c>innerHeight - outerHeight</c> here instead lands roughly one row short of the end, which
        /// looks like the newest message failing to scroll into view.
        /// </para>
        /// </summary>
        void ScrollToBottom()
        {
            scrollPosition = new Vector2(0f, int.MaxValue);
        }

        #endregion
    }

    /// <summary>
    /// A basic container for log details.
    /// </summary>
    struct Log
    {
        public int count;

        /// <summary>
        /// <c>count</c> rendered once, at the point it changes. The draw path runs on every layout
        /// and repaint pass over every visible row, so calling <c>count.ToString()</c> there
        /// allocated a string per row per pass. Written beside every assignment to <c>count</c>;
        /// <see cref="CountText"/> falls back to a live conversion if one is ever missed.
        /// </summary>
        public string countText;

        public string CountText => countText ?? count.ToString();

        public string message;
        public string stackTrace;
        public LogType type;

        /// <summary>
        /// The max string length supported by UnityEngine.GUILayout.Label without triggering
        /// "String too long for TextMeshGenerator. Cutting off characters."
        /// </summary>
        const int maxMessageLength = 16382;

        public bool Equals(Log log)
        {
            return message == log.message && stackTrace == log.stackTrace && type == log.type;
        }

        public string GetTruncatedMessage()
        {
            if (string.IsNullOrEmpty(message)) return message;
            return message.Length <= maxMessageLength ? message : message.Substring(0, maxMessageLength);
        }
    }

    /// <summary>
    /// Thread-safe queue for passing log messages from background threads to the Unity main thread.
    /// </summary>
    class ConcurrentQueue<T>
    {
        readonly Queue<T> queue     = new Queue<T>();
        readonly object   queueLock = new object();

        public void Enqueue(T item)
        {
            lock (queueLock) { queue.Enqueue(item); }
        }

        public bool TryDequeue(out T result)
        {
            lock (queueLock)
            {
                if (queue.Count == 0) { result = default(T); return false; }
                result = queue.Dequeue();
                return true;
            }
        }
    }
}
