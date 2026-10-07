/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * SocketsSettings can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class ApiSettings
    {
        public bool isActive = false;
        public bool doLogs;
        public string api_Host;
        public string api_Dev_Host;
        public int maxNumberOfDisplayErrors = 3;

        /// <summary>
        /// Seconds before a request is abandoned. There was no timeout at all before, so a
        /// server that accepted the connection and then stopped talking hung the call forever.
        ///
        /// An **absent** key keeps this 30: <c>JsonUtility.FromJson</c> constructs the object, so the
        /// initializer stands. 0 therefore only arrives if an operator wrote it - and 0 means "wait
        /// forever" to UnityWebRequest, an unbounded wait, so <see cref="Api"/> does not honour it.
        /// It also caps large values for the same reason.
        ///
        /// The AssetDownload and TextureCache blocks share the field name and no longer disagree
        /// about 0 - every site clamps through <c>WebRequestExtensions</c> - but they do not share
        /// the numbers, and the difference is deliberate. There 0 resolves to the *ceiling* rather
        /// than to 30, because on those blocks 0 has always meant "no limit" and the nearest bounded
        /// reading of that is the largest legal wait, not the smallest. Their ceiling is also far
        /// higher: a download cut short cannot resume.
        /// </summary>
        /// <remarks>
        /// Initialised from the const the resolver uses, not from a literal 30. The claim above -
        /// that an absent key and a typed 0 both end up on this field's own default - is only true
        /// while the two agree, and nothing else would notice them drifting apart.
        /// </remarks>
        public int timeout = WebRequestExtensions.ApiUnconfiguredSeconds;
    }
}
