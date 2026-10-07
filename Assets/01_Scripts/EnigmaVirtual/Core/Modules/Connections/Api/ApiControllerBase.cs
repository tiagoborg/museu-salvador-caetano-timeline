using JetBrains.Annotations;
using System;
using UnityEngine;

namespace EnigmaVirtual
{
    public enum ApiResponseResult { None, Calling, Success, Fail, Unknown }

    public abstract class ApiControllerBase : Api
    {
        [Header("Debug")]
        [SerializeField]
        protected bool DevelopmentEnvironment = false;

        [SerializeField]
        protected ApiResponseResult _lastCallResult = ApiResponseResult.None;

        protected int _numErrors = 0;

        /// <summary>
        /// Initializes Api Controller
        /// </summary>
        protected virtual void Init()
        {
            Development.LogIf(Core.settings.Api.doLogs, "Init!");

            // Once, here, rather than once per call from the Set-Cookie handler. With no
            // sessionName nothing is ever captured, so the per-call version reported on every
            // request forever - and only when the server actually answered, so a dead endpoint
            // said nothing at all.
            ReportMissingSessionName();

            _lastCallResult = ApiResponseResult.None;
            HostUrl = DevelopmentEnvironment ? Core.settings.Api.api_Dev_Host : Core.settings.Api.api_Host;
        }

        /// <summary>
        /// Api call.
        /// </summary>
        /// <param name="parameters"></param>
        public void DoApiCall(APIParameters parameters, [NotNull] Action<APIResponseModel> callback, string RestApiIncrement = "", HTTPMethods method = HTTPMethods.Post)
        {
            _lastCallResult = ApiResponseResult.Calling;
            DoCall(parameters, callback, OnResult, RestApiIncrement, method);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="result"></param>
        private void OnResult(ApiResponseResult result)
        {
            _lastCallResult = result;
        }
    }
}