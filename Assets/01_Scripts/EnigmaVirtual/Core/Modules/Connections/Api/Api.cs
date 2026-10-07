//#define DO_LOGS // use this on classes to print logs.
using EnigmaVirtual.Notifications;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public abstract class Api : MonoBehaviour
    {
        [SerializeField]
        private string headerRequest = "X-CSRF-TOKEN";
        /// <summary>
        /// The name of the session cookie this server sets, e.g. <c>PHPSESSID</c> or
        /// <c>laravel_session</c>. <b>Per project - there is no sensible default.</b>
        /// </summary>
        /// <remarks>
        /// This shipped as <c>x_plora_almeida_session</c> - one client's cookie, in the class every
        /// branched project inherits. Any other project either overrode it or, far more likely,
        /// never noticed and got the error at <c>SetSessionCookie</c> instead.
        ///
        /// Empty rather than a plausible-looking guess, and refused explicitly where it is read:
        /// <c>cookie.IndexOf("")</c> returns 0, so leaving it empty without a guard would capture
        /// the ENTIRE Set-Cookie header as the session and send a malformed cookie on every later
        /// call, silently. An empty default has to fail loudly to be safer than a wrong one.
        /// </remarks>
        [SerializeField]
        [Tooltip("Name of the session cookie this server sets (e.g. PHPSESSID). Required - there is "
            + "no default, because it is per project.")]
        private string sessionName = "";

        protected string HostUrl = string.Empty;

        /// <summary>
        /// Reports a missing <see cref="sessionName"/> once, at initialisation.
        /// </summary>
        /// <remarks>
        /// Once at boot rather than once per call. With sessionName empty no cookie is ever
        /// captured, so a per-call report fired on every request forever - and only when the server
        /// actually sent a Set-Cookie, so a dead endpoint said nothing at all. Here it reports
        /// either way. Protected because sessionName is private to this class and Init lives
        /// on ApiControllerBase.
        /// </remarks>
        protected void ReportMissingSessionName()
        {
            if (string.IsNullOrEmpty(sessionName))
                Development.LogError($"{GetType().Name}: sessionName is not set, so no session "
                    + "cookie can be captured and every authenticated call will go out "
                    + "unauthenticated. Set it to the name of the cookie this server issues.");
        }

        /// <summary>
        /// Names of the cookies in a Set-Cookie header, without their values.
        /// </summary>
        /// <remarks>
        /// A Set-Cookie header <b>is</b> the session token, and must never reach the log: this
        /// project ships <c>DebugSettings.Console.isActive: true</c> with Tab as the toggle and
        /// shake-to-open, and <c>Console</c> subscribes <c>logMessageReceivedThreaded</c> - so a
        /// visitor pressing Tab on a kiosk would read a replayable credential off the screen, and
        /// anyone with the machine reads it out of <c>Player.log</c>. Naming the cookies present is
        /// all the diagnostic needs.
        /// </remarks>
        /// <summary>
        /// Index at which <paramref name="name"/>'s cookie starts in a Set-Cookie header, or -1.
        /// </summary>
        /// <remarks>
        /// A cookie name starts at the beginning of the header or straight after a <c>;</c> or
        /// <c>,</c> separator - nowhere else. Searching for <c>name + "="</c> alone is NOT enough
        /// and that is not a theoretical objection: with <c>sessionName = "session"</c>, the header
        /// <c>my_session=WRONG; Path=/, session=RIGHT</c> matches inside <c>my_session=</c> and
        /// captures the wrong cookie's value. Caught by the counter-test for this fix, not by
        /// reading it.
        /// </remarks>
        private static int FindCookieStart(string setCookieHeader, string name)
        {
            if (string.IsNullOrEmpty(setCookieHeader) || string.IsNullOrEmpty(name))
                return -1;

            string needle = name + "=";
            int from = 0;

            while (from <= setCookieHeader.Length - needle.Length)
            {
                int at = setCookieHeader.IndexOf(needle, from, StringComparison.Ordinal);
                if (at < 0)
                    return -1;

                int before = at - 1;
                while (before >= 0 && setCookieHeader[before] == ' ')
                    before--;

                if (at == 0 || (before >= 0 && (setCookieHeader[before] == ';' || setCookieHeader[before] == ',')))
                    return at;

                from = at + needle.Length;
            }

            return -1;
        }

        /// <summary>
        /// Attribute names that appear in a Set-Cookie header but are not cookies.
        /// </summary>
        private static readonly string[] CookieAttributes =
        {
            "Path", "Domain", "Expires", "Max-Age", "SameSite", "Secure", "HttpOnly", "Version", "Comment"
        };

        private static string CookieNames(string setCookieHeader)
        {
            if (string.IsNullOrEmpty(setCookieHeader))
                return "<empty>";

            string[] parts = setCookieHeader.Split(';', ',');
            System.Text.StringBuilder names = new System.Text.StringBuilder();

            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0)
                    continue;

                string name = parts[i].Substring(0, eq).Trim();
                if (name.Length == 0)
                    continue;

                // Path=/ and friends are attributes, not cookies. Listing them buries the one or two
                // names the reader is actually looking for.
                bool isAttribute = false;
                for (int a = 0; a < CookieAttributes.Length; a++)
                {
                    if (string.Equals(name, CookieAttributes[a], StringComparison.OrdinalIgnoreCase))
                    {
                        isAttribute = true;
                        break;
                    }
                }

                if (isAttribute)
                    continue;

                if (names.Length > 0)
                    names.Append(", ");

                names.Append(name);
            }

            return names.Length > 0 ? names.ToString() : "<no named cookies>";
        }

        // fileName, folderPath and localFilePath used to live here, as instance fields. They were
        // assigned at the top of DoCallNative and read again *after* `yield return
        // www.SendWebRequest()`, so a second call starting while the first was in flight overwrote
        // all three - and the first response was then cached under the second call's filename, or
        // its error path loaded the wrong cached file. Only localFilePath survived the move to
        // locals: folderPath is derived from it where it is used, and fileName only ever existed to
        // build it.
        private string customMediaPath = "jsons";
        private string sessionCookie = "";

        /// <summary>
        /// Makes a call to service.
        /// </summary>
        /// <param name="parameters">Parameters</param>
        /// <param name="callback">Callback listenner.</param>
        /// <param name="onEnded">End Action listenner.</param>
        /// <param name="RestApiIncrement"></param>
        /// <param name="method">Http method.</param>
        protected void DoCall(APIParameters parameters, Action<APIResponseModel> callback, Action<ApiResponseResult> onEnded, string RestApiIncrement, HTTPMethods method = HTTPMethods.Post)
        {
            StartCoroutine(DoCallNative(parameters, callback, onEnded, RestApiIncrement, method));
        }

        private IEnumerator DoCallNative(APIParameters parameters, Action<APIResponseModel> callback, Action<ApiResponseResult> onEnded, string RestApiIncrement, HTTPMethods method = HTTPMethods.Post)
        {
            string currentCall = HostUrl + RestApiIncrement;
            string csrf_token = "";

            Development.LogIf(Core.settings.Api.doLogs, $"{currentCall} method: {method.ToString()}");

            string fileName = StringUtil.GetFileNameFromUrl(HostUrl + RestApiIncrement + ".json");
            string localFilePath = !string.IsNullOrEmpty(customMediaPath) ? Application.persistentDataPath + "/" + customMediaPath + "/" + fileName : Application.persistentDataPath + "/" + fileName + ".json";

            WWWForm form = new WWWForm();
            for (int i = 0; i < parameters.FieldNames.Count; i++)
            {
                if (parameters.FieldNames[i] == "_token")
                {
                    csrf_token = parameters.FieldValues[i];
                }
                form.AddField(parameters.FieldNames[i], parameters.FieldValues[i]);
            }
            form.AddField("ContentLength", "0");

            UnityWebRequest www;
            switch (method)
            {
                case HTTPMethods.Get:
                    // '=' between name and value, '&' between pairs, both halves escaped. The old
                    // loop concatenated name and value with no separator at all, so two parameters
                    // produced "?a1b2" - not a malformed query so much as not a query, which is why
                    // no GET in this framework has ever worked. Escaping is the other half: an
                    // unescaped value containing & ? or # rewrites the request.
                    StringBuilder query = new StringBuilder();

                    // '?' only if the increment did not already bring one. A RestApiIncrement carrying
                    // "?x=1" otherwise produced "...?x=1?a=b", and everything after the second '?' is
                    // part of x's value - so every parameter this loop appends was silently swallowed.
                    // No caller does that today; the cost of being wrong about that later is a GET that
                    // looks correct and sends nothing.
                    //
                    // Ordinal on purpose - an escaped %3F is not a query start and must not count. Not
                    // handled, and unchanged from before: a '?' inside a '#' fragment. Appending there
                    // is equally useless either way, so this is not a regression, but the separator
                    // logic is not complete and should not be read as if it were.
                    bool alreadyHasQuery = currentCall.IndexOf('?') >= 0;

                    for (int i = 0; i < parameters.FieldNames.Count; i++)
                    {
                        query.Append(query.Length == 0 && !alreadyHasQuery ? '?' : '&')
                             .Append(UnityWebRequest.EscapeURL(parameters.FieldNames[i]))
                             .Append('=')
                             .Append(UnityWebRequest.EscapeURL(parameters.FieldValues[i] ?? string.Empty));
                    }

                    // Starting empty rather than at "?" also drops the bare trailing "?" that a
                    // zero-parameter GET used to emit.
                    www = UnityWebRequest.Get(currentCall + query);
                    break;
                case HTTPMethods.Post:
                default:
                    www = UnityWebRequest.Post(currentCall, form);
                    break;
            }

            // Disposed on every path through the body below, which is where the native handle and
            // the download buffer used to leak on every single call. `using` lowers to
            // try/finally, and a finally is legal around a `yield return` - only a `catch` is not
            // (CS1626), and the catches further down sit inside their own trys.
            //
            // Deliberately NOT claimed: a coroutine killed by StopCoroutine or by destroying this
            // component. Unity drops the iterator without disposing it, so the finally does not run
            // and that request still leaks. Unverified on 6000.3 and left alone - the leak worth
            // fixing was the one on the ordinary path, which happened every time.
            using (www)
            {
                // A missing "timeout" key does not arrive as 0 - JsonUtility.FromJson constructs the
                // object, so the field initializer stands and an older settings.json simply gets 30.
                // 0 only arrives if an operator typed it, and to UnityWebRequest 0 is "wait forever",
                // which is an unbounded wait. The cap is there for the same failure by other means: a
                // mistyped 86400 would restore the unbounded wait just as effectively.
                //
                // Unguarded, like every other Core.settings read in this class. The invariant is that
                // this module cannot exist before the settings parse, and the shortest proof is that
                // MainController.Init() reads Core.settings.Api.isActive to decide whether to create the
                // controller at all - so a live ApiController is itself evidence that settings.Api was
                // dereferenced successfully. Init() in turn runs only from Core.CoreSetupComplete().
                //
                // Supporting, and weaker: no type in this hierarchy declares a Unity message that would
                // run at scene load, and none is authored in any scene - so there is no instance ahead of
                // Init() by either route. Prefer the isActive argument; the authoring one is a fact about
                // today's scenes and an editor button can change it.
                //
                // Do not "make this safe" with `?.`: it cannot be, and pretending otherwise is worse
                // than the NRE. Api.cs's `if (Core.settings.Api.doLogs)` below sits outside any
                // [Conditional] and ApiControllerBase dereferences settings unconditionally and runs
                // first, so a null here has already crashed. A `?? false` would turn a boot-order
                // violation with a stack trace into silently absent logging.
                www.SetApiTimeout(Core.settings.Api.timeout);

                www.SetRequestHeader("Accept", "application/json");

                // Only once there is something to send. Whether Unity rejects an empty header value is
                // version-specific and was never confirmed; not sending an empty one removes the
                // question instead of answering it. Note the separate, still-open question of
                // whether Unity honours a `cookie` request header at all.
                if (!string.IsNullOrEmpty(sessionCookie))
                    www.SetRequestHeader("cookie", sessionCookie);

                if (csrf_token != "")
                {
                    www.SetRequestHeader(headerRequest, csrf_token);

                    // Length only. The token itself used to go into Player.log whenever doLogs was
                    // on, and Assert logs when its condition is TRUE, so that was the on switch.
                    Development.LogIf(Core.settings.Api.doLogs, $"AddToken: {csrf_token.Length} chars");
                }

                yield return new WaitForEndOfFrame();
                yield return www.SendWebRequest();

                string cookie = www.GetResponseHeader("Set-Cookie");

                if (!string.IsNullOrEmpty(cookie) && sessionCookie == "")
                {
                    // >= 0, not > 0. IndexOf returns 0 when the session cookie is the first entry in
                    // Set-Cookie - the common case - so the old test rejected precisely the response
                    // it was written to accept, and sessionCookie was never captured at all. That is
                    // also why the header above was always empty.
                    // Anchored on a cookie BOUNDARY, not merely on "name=" - see
                    // FindCookieStart. Empty sessionName is reported once at Init rather than here.
                    int index = FindCookieStart(cookie, sessionName);

                    if (index >= 0)
                    {
                        // Cut at the first ';' rather than running to the end of the header.
                        // Substring(index) alone swept up Path, HttpOnly, Expires and every other
                        // cookie Unity concatenated after this one - and all of it was then sent
                        // back as the Cookie request header.
                        int end = cookie.IndexOf(';', index);
                        sessionCookie = end > index
                            ? cookie.Substring(index, end - index)
                            : cookie.Substring(index);
                    }
                    else if (!string.IsNullOrEmpty(sessionName))
                    {
                        // LogError, and not behind doLogs. "Verify session name!" at LogWarning, gated
                        // on a flag that ships false, meant every later call went out unauthenticated
                        // with nothing whatsoever in the log to say why.
                        //
                        // Cookie NAMES only. The header is the session token, and this log is always
                        // compiled and reachable from the in-app console.
                        Development.LogError($"the Set-Cookie header carried no [{sessionName}] entry, "
                            + "so no session was captured and every later call goes out "
                            + "unauthenticated. Check the sessionName field on this component against "
                            + $"the cookies the server actually sent: {CookieNames(cookie)}");
                    }
                }

                APIResponseModel response = new APIResponseModel();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    // The `if (www != null)` that used to wrap all of this, and the else it carried,
                    // are gone: the test one line up already dereferences www, so the null branch was
                    // unreachable and its duplicate copy of this bookkeeping was never once executed.
                    Core.notificationManager?.ShowNotification(NotificationManager.NotificationType.Error, "API ERROR", "system_request_error");

                    Development.LogError("" + currentCall + "  " + www.responseCode.ToString());
                    Development.LogError("" + currentCall + "  " + (www.error != null ? www.error : "No error message!"));
                    Development.LogError("Raw message: " + www.downloadHandler?.text);

                    response.code = APIResponseCode.Error;
                    response.msg = "system_request_error";
                    response.raw = "";

                    // One condition, not two nested ones. An existing but *empty* cache file used to
                    // satisfy the outer if and fail the inner one, falling out of both with neither
                    // callback nor onEnded invoked - so the caller waited for a response that could
                    // never arrive, and the only trace was the three LogErrors above, which look like
                    // an ordinary handled failure. Zero-byte files are not hypothetical here: the
                    // truncate-then-append writer this class used to have left exactly that behind
                    // when a write threw between the two calls.
                    string localJson;
                    ApiResponseResult cachedResult = ApiResponseResult.Fail;

                    if (LoadJsonFile(localFilePath, out localJson, out DateTime cachedWrittenUtc)
                        && !string.IsNullOrEmpty(localJson))
                    {
                        try
                        {
                            APIResponseModel cached = JsonUtility.FromJson<APIResponseModel>(localJson);

                            // FromJson returns null for a body of literally "null". Assigning .raw on
                            // it would NRE into the catch below, which would then hand the caller that
                            // null instead of the Error model built above.
                            if (cached != null)
                            {
                                cached.raw = localJson;

                                // Mark it, then say it. Before this the caller got a Success carrying
                                // content it had no way to date, so an endpoint dead for a week was
                                // indistinguishable from one that answered - and with a bounded
                                // timeout making an unreachable server the *normal* failure rather
                                // than a rarity, that is the ordinary outcome, not an edge case.
                                cached.fromCache = true;
                                cached.cacheAge = cachedWrittenUtc == default
                                    ? (TimeSpan?)null
                                    : DateTime.UtcNow - cachedWrittenUtc;

                                response = cached;
                                cachedResult = ApiResponseResult.Success;

                                // LogError, and ungated. This is the line that says the screen is
                                // showing old content: it is not a failure the caller has to handle -
                                // the content is real and rendering it is correct - but nobody can act
                                // on staleness they cannot see.
                                string age = cached.cacheAge == null
                                    ? "of unknown age (its timestamp could not be read)"
                                    : $"written {StringUtil.FormatElapsed(cached.cacheAge.Value)} ago";

                                Development.LogError($"[{currentCall}] failed, so a CACHED response "
                                    + $"{age} is being served instead. The screen is showing content "
                                    + "from that time, not from the server, and it will keep doing so "
                                    + "for as long as the call keeps failing. Check the endpoint.");
                            }
                            else
                            {
                                Development.LogError($"the cached response at [{localFilePath}] parsed "
                                    + "to null, so the stale copy was no use either.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Core.notificationManager?.ShowNotification(NotificationManager.NotificationType.Error, "Error parsing api JSON", ex.Message);
                            Development.LogError("Error parsing api JSON: " + ex.Message);
                        }
                    }

                    // Outside the try, and reached exactly once on every path. These used to be
                    // invoked from inside it, so a caller whose own callback threw was caught by this
                    // method's catch and then invoked a *second* time - callback twice, and
                    // onEnded(Fail) after onEnded(Success) had already fired.
                    callback?.Invoke(response);
                    onEnded?.Invoke(cachedResult);
                }
                else
                {
                    if (www.isDone)
                    {
                        ApiResponseResult result = ApiResponseResult.Fail;

                        try
                        {
                            APIResponseModel parsed = JsonUtility.FromJson<APIResponseModel>(www.downloadHandler.text);

                            // Same two reasons as the cache branch above: a null from FromJson must not
                            // reach the caller in place of the response model, and the callbacks must
                            // not sit inside a try that would re-invoke them if they threw.
                            if (parsed != null)
                            {
                                parsed.raw = www.downloadHandler.text;
                                response = parsed;
                                result = ApiResponseResult.Success;

                                SaveAsJsonFile(www.downloadHandler.text, localFilePath);
                            }
                            else
                            {
                                response.code = APIResponseCode.Error;
                                response.msg = "system_request_error";

                                Development.LogError("the API returned a body that parsed to null, so "
                                    + $"there is nothing to hand back: [{www.downloadHandler.text}]");
                            }
                        }
                        catch (Exception ex)
                        {
                            Core.notificationManager?.ShowNotification(NotificationManager.NotificationType.Error, "Error parsing api JSON", ex.Message);
                            Development.LogError("Error parsing api JSON: " + ex.Message);
                        }

                        callback?.Invoke(response);
                        onEnded?.Invoke(result);
                    }
                    else
                    {
                        Core.notificationManager?.ShowNotification(NotificationManager.NotificationType.Error, "CALL ERROR", www.responseCode + ":" + www.error);

                        Development.LogError("" + HostUrl + RestApiIncrement + "      " + "CALL ERROR" + www.responseCode + ":" + www.error);

                        response.code = APIResponseCode.BadRequest;
                        response.msg = www.responseCode.ToString();
                        response.raw = www.error;
                        if (callback != null) callback(response);
                        if (onEnded != null) onEnded(ApiResponseResult.Fail);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves a cache path and refuses anything that lands outside <c>persistentDataPath</c>.
        ///
        /// <c>StringUtil.GetFileNameFromUrl</c> handles both separators as of v0.33.0, so the
        /// specific hole this was written for - a backslash in <c>api_Host</c> or the call increment
        /// surviving into the "filename" and walking up out of the cache folder - is closed at
        /// source. The guard stays regardless: the input is a composed path from settings, the check
        /// is two calls, and refusing beats writing API responses into arbitrary directories. Same
        /// reasoning as the containment guard on the cleanup root, and it is defence in depth rather
        /// than the only thing standing there.
        /// </summary>
        private static bool TryResolveCachePath(string path, out string fullPath)
        {
            fullPath = string.Empty;

            string root = Path.GetFullPath(Application.persistentDataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            // GetFullPath throws on invalid characters, which a URL-derived name can easily contain
            // (':' and '?' among them) - so this is a refusal, not an exception out of the coroutine.
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception e)
            {
                Development.LogError($"the cache path [{path}] could not be resolved, so the local "
                    + $"copy was skipped: {e.Message}");
                return false;
            }

            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Development.LogError($"REFUSED the cache path [{fullPath}]: it resolves outside "
                    + $"[{root}]. Check api_Host and the call increment for a stray backslash.");
                fullPath = string.Empty;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads a previously cached response. <paramref name="localFilePath"/> is a parameter rather
        /// than the instance field it used to read, so a call still in flight cannot repoint it.
        /// </summary>
        /// <summary>
        /// Reads a previously cached response, and reports when the file was last written - which is
        /// the age of the content the caller is about to be handed.
        /// </summary>
        /// <remarks>
        /// The timestamp comes from in here because <see cref="TryResolveCachePath"/> may redirect to
        /// a different file than the caller asked for, and the age of the wrong file is worse than no
        /// age at all.
        /// <para>
        /// One method, not an overload pair. The first version kept a 2-arg overload delegating to
        /// this one - the right shape for a *public* API where callers must keep compiling, but this
        /// is private with a single caller, so it was compatibility scaffolding for a problem that
        /// cannot arise, and it had zero callers the moment it was written.
        /// </para>
        /// </remarks>
        private bool LoadJsonFile(string localFilePath, out string rawData, out DateTime writtenUtc)
        {
            writtenUtc = default;

            if (!TryResolveCachePath(localFilePath, out localFilePath))
            {
                rawData = string.Empty;
                return false;
            }

            Development.LogIf(Core.settings.Api.doLogs, "Trying to load file localy at: " + localFilePath);
            Development.LogIf(Core.settings.Api.doLogs, "Does file exists localy? " + File.Exists(localFilePath));

            if (!File.Exists(localFilePath))
            {
                if (Core.settings.Api.doLogs)
                    Development.LogWarning("File not found in local file system!");

                rawData = string.Empty;
                return false;
            }

            // File.ReadAllText, not a bare StreamReader with Close() outside a finally: a throw in
            // ReadToEnd leaked the handle onto the config directory, and the next write to the same
            // file failed with a sharing violation - the same shape as FileManager.
            try
            {
                rawData = File.ReadAllText(localFilePath);

                // Best effort, and separately guarded: a readable file whose timestamp cannot be
                // stat'd should still be served. An age of default just means "unknown", which the
                // caller words differently rather than treating as fresh.
                try { writtenUtc = File.GetLastWriteTimeUtc(localFilePath); }
                catch { writtenUtc = default; }
            }
            catch (Exception e)
            {
                Development.LogError($"Could not read the cached file [{localFilePath}]: {e.Message}");
                rawData = string.Empty;
                return false;
            }

            Development.LogIf(Core.settings.Api.doLogs, "File loaded from local file system!");

            return true;
        }

        /// <summary>
        /// Caches a response to disk.
        ///
        /// <para><b>Breaking change for branched projects.</b> This was
        /// <c>protected void SaveAsJsonFile(string rawData, string folderPath, string filename)</c>,
        /// where <c>filename</c> was **entirely unused**: the method created the directory from its
        /// <c>folderPath</c> argument but wrote to the instance field <c>localFilePath</c>, so its
        /// signature described something it did not do. Both are gone. Dropping the argument rather
        /// than redefining it is deliberate - a same-shape signature whose third argument silently
        /// changed meaning would still compile in a derived project and quietly write somewhere
        /// else. And two parameters for one location, with nothing making them agree, is the shape
        /// of the query-building defect this pass removed.</para>
        ///
        /// <para>Now <c>private</c> too: the only caller is in this class, and this is the one
        /// commit where narrowing it is free. Re-widen if a project turns out to need it.</para>
        /// </summary>
        private void SaveAsJsonFile(string rawData, string filePath)
        {
            if (!TryResolveCachePath(filePath, out filePath))
                return;

            Development.LogIf(Core.settings.Api.doLogs, "Saving file localy at: " + filePath);

            // Derived from the path actually written, so the two cannot disagree. CreateDirectory is
            // already a no-op on an existing directory, so the Directory.Exists test it used to sit
            // behind bought nothing.
            string folderPath = Path.GetDirectoryName(filePath);

            try
            {
                if (!string.IsNullOrEmpty(folderPath))
                    Directory.CreateDirectory(folderPath);
            }
            catch (Exception e)
            {
                Development.LogError($"could not create the cache folder [{folderPath}], so the "
                    + $"response was not cached: {e.Message}");
                return;
            }


            // ConfigManager.WriteAtomic, which is the framework's one write-then-replace routine.
            // A plain WriteAllText truncates first, so an interruption left a half-written cache
            // that File.Exists still reports as present - and the error branch above then feeds it to
            // JsonUtility as though it were a good response. WriteAtomic writes a .tmp and swaps, logs
            // its own failure, and cleans up after itself.
            //
            // AssetManager's `.part` is not a duplicate of this - that temp path is the download
            // *target*, written by UnityWebRequest over the download's lifetime, which a routine
            // taking finished content as a string cannot express.
            //
            // No try/catch of its own, and the bool is discarded on purpose: WriteAtomic catches, names
            // the path and the reason, and removes its own leftover .tmp, and there is no alternative
            // action available here - caching is best-effort.
            //
            // Not because the response has already reached the callback - it has not: this runs at
            // DoCallNative's success branch *before* callback?.Invoke and
            // *inside* its try, so a throw here would have discarded a good response and reported it as
            // "Error parsing api JSON". Safe only because WriteAtomic cannot throw - which is now the
            // actual reason rather than a wrong one.
            // Best effort while the endpoint answers, and NOT best effort once it stops: this file is
            // the offline fallback this same class reads on a failed request. Losing the write costs
            // nothing today and costs the fallback tomorrow, which is why the context says so rather
            // than calling it discardable - the bool is not checked, so this line is the only notice.
            ConfigManager.WriteAtomic(filePath, rawData,
                "caching an API response - best effort, but it is the offline fallback, so the next "
                + "failed call will have nothing to serve");
        }
    }

    /// <summary>
    /// Some supported methods described in the rfc: http://www.w3.org/Protocols/rfc2616/rfc2616-sec9.html#sec9
    /// </summary>
    public enum HTTPMethods : byte
    {
        /// <summary>
        /// The GET method means retrieve whatever information (in the form of an entity) is identified by the Request-URI.
        /// If the Request-URI refers to a data-producing process, it is the produced data which shall be returned as the
        /// entity in the response and not the source text of the process, unless that text happens to be the output of the process.
        /// </summary>
        Get,

        /// <summary>
        /// The HEAD method is identical to GET except that the server MUST NOT return a message-body in the response.
        /// The metainformation contained in the HTTP headers in response to a HEAD request SHOULD be identical to the information sent in response to a GET request.
        /// This method can be used for obtaining metainformation about the entity implied by the request without transferring the entity-body itself.
        /// This method is often used for testing hypertext links for validity, accessibility, and recent modification.
        /// </summary>
        Head,

        /// <summary>
        /// The POST method is used to request that the origin server accept the entity enclosed in the request as a new subordinate of the resource identified by the Request-URI in the Request-Line.
        /// POST is designed to allow a uniform method to cover the following functions:
        /// <list type="bullet">
        ///     <item><description>Annotation of existing resources;</description></item>
        ///     <item><description>Posting a message to a bulletin board, newsgroup, mailing list, or similar group of articles;</description></item>
        ///     <item><description>Providing a block of data, such as the result of submitting a form, to a data-handling process;</description></item>
        ///     <item><description>Extending a database through an append operation.</description></item>
        /// </list>
        /// The actual function performed by the POST method is determined by the server and is usually dependent on the Request-URI.
        /// The posted entity is subordinate to that URI in the same way that a file is subordinate to a directory containing it,
        /// a news article is subordinate to a newsgroup to which it is posted, or a record is subordinate to a database.
        /// The action performed by the POST method might not result in a resource that can be identified by a URI. In this case,
        /// either 200 (OK) or 204 (No Content) is the appropriate response status, depending on whether or not the response includes an entity that describes the result.
        /// </summary>
        Post,

        /// <summary>
        /// The PUT method requests that the enclosed entity be stored under the supplied Request-URI.
        /// If the Request-URI refers to an already existing resource, the enclosed entity SHOULD be considered as a modified version of the one residing on the origin server.
        /// If the Request-URI does not point to an existing resource, and that URI is capable of being defined as a new resource by the requesting user agent,
        /// the origin server can create the resource with that URI. If a new resource is created, the origin server MUST inform the user agent via the 201 (Created) response.
        /// If an existing resource is modified, either the 200 (OK) or 204 (No Content) response codes SHOULD be sent to indicate successful completion of the request.
        /// If the resource could not be created or modified with the Request-URI, an appropriate error response SHOULD be given that reflects the nature of the problem.
        /// The recipient of the entity MUST NOT ignore any Content-* (e.g. Content-Range) headers that it does not understand or implement and MUST return a 501 (Not Implemented) response in such cases.
        /// </summary>
        Put,

        /// <summary>
        /// The DELETE method requests that the origin server delete the resource identified by the Request-URI. This method MAY be overridden by human intervention (or other means) on the origin server.
        /// The client cannot be guaranteed that the operation has been carried out, even if the status code returned from the origin server indicates that the action has been completed successfully.
        /// However, the server SHOULD NOT indicate success unless, at the time the response is given, it intends to delete the resource or move it to an inaccessible location.
        /// A successful response SHOULD be 200 (OK) if the response includes an entity describing the status, 202 (Accepted) if the action has not yet been enacted, or 204 (No Content)
        /// if the action has been enacted but the response does not include an entity.
        /// </summary>
        Delete,

        /// <summary>
        /// http://tools.ietf.org/html/rfc5789
        /// The PATCH method requests that a set of changes described in the request entity be applied to the resource identified by the Request-URI.
        /// The set of changes is represented in a format called a "patchdocument" identified by a media type. If the Request-URI does not point to an existing resource,
        /// the server MAY create a new resource, depending on the patch document type (whether it can logically modify a null resource) and permissions, etc.
        /// </summary>
        Patch,

        /// <summary>
        /// The HTTP methods PATCH can be used to update partial resources. For instance, when you only need to update one field of the resource, PUTting a complete resource representation might be cumbersome and utilizes more bandwidth.
        /// <seealso href="http://restcookbook.com/HTTP%20Methods/patch/"/>
        /// </summary>
        Merge,

        Options
    }

    public enum APIResponseCode
    {
        Error = -10,
        Success = 0,
        Error2 = 1,
        WrongCredentials = 3,
        TooManyAttempts = 2,
        BadRequest = 400
    }

    [Serializable]
    public class APIParameters
    {
        public List<string> FieldNames = new List<string>();
        public List<string> FieldValues = new List<string>();

        public void AddField(string field, string value)
        {
            FieldNames.Add(field);
            FieldValues.Add(value);
        }

        public void AddField(string field, int value)
        {
            FieldNames.Add(field);
            FieldValues.Add(value.ToString());
        }

        public void AddField(string field, int[] value)
        {
            AddField(field, JsonConvert.SerializeObject(value));
        }
    }

    [Serializable]
    public class APIResponseModel
    {
        public APIResponseCode code;
        public string msg;

        /// <summary>
        /// True when this response was read back from the on-disk cache after the live call failed,
        /// rather than received from the server.
        /// </summary>
        /// <remarks>
        /// The reason this exists: a cache hit is delivered as <c>ApiResponseResult.Success</c>, which
        /// is right - the caller *did* get content and should render it - but it left no way to tell
        /// yesterday's content from today's, so an endpoint that had been dead for a week looked
        /// exactly like one that answered. The result stays <c>Success</c> deliberately; the
        /// staleness rides here instead, so no existing caller changes behaviour - and unlike a new
        /// <c>ApiResponseResult</c> member, which would break every branch's <c>switch</c>, adding a
        /// field breaks nothing.
        /// <para>
        /// <b>Scope, precisely.</b> This is set by <see cref="Api"/>'s cache-fallback branch and by
        /// <c>AppDataController</c>'s offline appData path, and nowhere else - so it means "read from
        /// a file rather than received from the server", across both layers. Note the appData layer
        /// also carries <c>failedRemoteJson</c>, which is a narrower thing: that one is the cleanup guard
        /// saying the keep-list cannot be trusted. Two signals, two jobs.
        /// <para>
        /// <c>[NonSerialized]</c> is load-bearing, not tidiness. <c>JsonUtility</c> matches by field
        /// name, so a public serialized field would let a *server* set <c>"fromCache": true</c> in its
        /// own payload - and, worse, a cached body that had been through a round-trip could come back
        /// claiming to be fresh. This flag is set by the code that read the file and by nothing else.
        /// </para>
        /// </remarks>
        [NonSerialized]
        public bool fromCache;

        /// <summary>
        /// How old the cached copy was when it was served. <c>null</c> when unknown - either this did
        /// not come from a cache, or its timestamp could not be read.
        /// </summary>
        /// <remarks>
        /// Nullable rather than <c>default</c>, because <c>default</c> could not tell "unknown" from
        /// "zero seconds old". The log worded those two differently while the model conflated them, so
        /// a consumer writing <c>if (cacheAge > TimeSpan.FromHours(24))</c> would have shown nothing
        /// for exactly the un-datable cache the flag exists to expose.
        /// </remarks>
        [NonSerialized]
        public TimeSpan? cacheAge;

        private string _raw;

        public string raw
        {
            get { return _raw; }
            set
            {
                //_raw = StringUtil.StripTagsForUnity(value);
                _raw = value;
            }
        }

        /// <summary>
        /// Renames a member of <see cref="raw"/>, textually - every occurrence, at any depth.
        /// </summary>
        /// <remarks>
        /// A string replace, deliberately: it leaves the rest of the payload byte-for-byte alone, which
        /// matters because <c>AppDataController</c> hands <see cref="raw"/> straight to
        /// <c>ConfigManager.WriteAtomic</c> as the offline cache. Round-tripping it through a parser to
        /// rename one key would reformat and reorder the whole document.
        /// <para>
        /// The cost of that choice: it rewrites <b>every</b> occurrence, at <b>any</b> depth - a nested
        /// object with its own <c>values</c> member is renamed along with the root's. It is <i>not</i>
        /// reachable through a string value: a quote
        /// inside a JSON string is escaped, so <c>\"values\":</c> does not contain <c>"values":</c>.
        /// Nothing in this repo calls it.
        /// </para>
        /// </remarks>
        public void ChangeValueName(string newName, string oldName = "")
        {
            if (raw == null)
            {
                // Only null threw. Errors, not warnings, because a null body means the response was
                // never populated.
                Development.LogError("ChangeValueName called before any response body was set.");
                return;
            }

            if (raw.Length == 0)
            {
                // Empty is a value this file produces itself on its own error path, and "".Replace() was
                // always a harmless no-op - so this is a warning, not an error. Turning a benign call red
                // trains people to ignore the colour.
                Development.LogWarning("ChangeValueName called with an empty response body; nothing to rename.");
                return;
            }

            string old = string.IsNullOrEmpty(oldName) ? "values" : oldName;
            string token = "\"" + old + "\":";

            // string.Replace returns the input unchanged when there is no match, so without this the
            // method's whole job could fail to happen and say nothing - the likely causes being a
            // server-side rename, a typo in oldName, or a payload already renamed once.
            if (!raw.Contains(token))
            {
                Development.LogError($"ChangeValueName found no [{old}] member to rename, so the "
                    + $"payload is unchanged and anything reading [{newName}] will come back empty.");
                return;
            }

            raw = raw.Replace(token, "\"" + newName + "\":");
        }

        /// <summary>
        /// The <c>values</c> member of <see cref="raw"/>, as JSON. <c>null</c> when the body is missing,
        /// unparseable, or carries no <c>values</c> - each of which is logged at <c>LogError</c>, so it
        /// survives on every target rather than only where <c>DO_LOGS</c> is defined. Top-level only,
        /// where the old greedy regex would match <c>"values":</c> at any depth.
        /// </summary>
        /// <remarks>
        /// <b>This parsed JSON with a regex, and got it wrong in three ways</b>. The old body was
        /// one <c>Regex</c> plus <c>matches[0]</c>:
        /// <list type="number">
        /// <item>No match check. Any response without a <c>values</c> member - an error payload, an HTML
        /// error page, an empty body - threw <c>ArgumentOutOfRangeException</c> out of the
        /// <c>MatchCollection</c> indexer instead of reporting a missing field.</item>
        /// <item><c>.+</c> is greedy, so the capture ran to the <b>last</b> <c>}</c> or <c>]</c> on the
        /// line. It returned correct JSON only when <c>values</c> happened to be the final member of the
        /// root object - the trailing <c>Substring(0, s.Length - 1)</c> existed to chop the root's own
        /// closing brace back off. Put any member after <c>values</c> and it returned that too, minus one
        /// character.</item>
        /// <item><b>It returned silently truncated JSON on a pretty-printed body</b>, which is worse than
        /// either of the above and was measured rather than reasoned about. <c>.</c> does not match a
        /// newline without <c>RegexOptions.Singleline</c>, so on
        /// <c>{"code":0,(newline)"values":[{"a":1}](newline)}</c> the capture stopped at the end of
        /// its own line and
        /// the <c>-1</c> then ate the closing <c>]</c>, yielding <c>[{"a":1}</c>. No exception, no log,
        /// invalid JSON handed to the caller. For the same reason a single space after the colon -
        /// <c>"values": [</c> - matched nothing at all and threw.</item>
        /// <item>The <c>Regex</c> was constructed on every call, uncached and uncompiled.</item>
        /// </list>
        /// Newtonsoft was already a dependency of this file, and <c>AppDataController</c> parses this
        /// very string with <c>JObject</c> - so the regex was never the only option, just the one nobody
        /// revisited.
        /// </remarks>
        public string GetValueAsString()
        {
            if (string.IsNullOrEmpty(raw))
            {
                Development.LogError("GetValueAsString called with no response body.");
                return null;
            }

            JToken values;

            try
            {
                // DateParseHandling.None, because this re-serializes rather than slices. Newtonsoft
                // defaults to converting date-SHAPED strings into DateTime tokens, which would come
                // back out reformatted - "2026-08-12" as "2026-08-12T00:00:00" - silently rewriting
                // content on its way to the caller's parser. Numbers are still renormalised by the
                // round-trip ("1.50" becomes "1.5"), which is the same JSON value; a date turning into
                // a timestamp is not.
                using (JsonTextReader reader = new JsonTextReader(new StringReader(raw))
                {
                    DateParseHandling = DateParseHandling.None
                })
                {
                    values = JObject.Load(reader)["values"];
                }
            }
            catch (JsonException e)
            {
                // Caught rather than propagated: a malformed body is an ordinary outcome for a network
                // call, and the caller gets null. The message carries the parser's line and position.
                Development.LogError($"Response body is not valid JSON, so [values] cannot be read: {e.Message}");
                return null;
            }

            // JTokenType.Null as well as a C# null: a body carrying `"values": null` yields a JValue,
            // not null, so the reference check alone let it through and ToString returned the literal
            // four-character string "null" as a successful read. The caller's
            // DeserializeObject<T>("null") then returns null with no error anywhere. The old regex
            // required a [ or { and threw on this shape, so treating it as a hit would have been a
            // defect introduced by the fix.
            if (values == null || values.Type == JTokenType.Null)
            {
                // LogError, not LogWarning: this is the same class of failure as the two branches
                // above - the server did not send what the contract says - and LogWarning is
                // [Conditional("DO_LOGS")], which is defined for Android and Standalone only. On iOS
                // and WebGL a warning here would be compiled out entirely, leaving a null return with
                // no artefact on any platform.
                Development.LogError("Response body has no [values] member, or it is null.");
                return null;
            }

            // Formatting.None: the old form returned the server's own whitespace. This is a payload for
            // another parser, so compact is smaller and deterministic.
            //
            // Be aware it is a re-serialization, not a slice: escapes are normalised (a \uXXXX sequence comes
            // back as the literal character), exponents expand ("1e5" as "100000.0"), and duplicate keys
            // resolve last-wins. Equivalent JSON in every case, and fine for a re-parse - but a caller
            // that hashes or string-compares this output will not get the server's bytes back.
            return values.ToString(Formatting.None);
        }
    }
}
