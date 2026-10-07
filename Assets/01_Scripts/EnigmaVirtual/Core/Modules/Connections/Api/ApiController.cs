namespace EnigmaVirtual
{
    public class ApiController : ApiControllerBase
    {
        public new void Init()
        {
            base.Init();

            _lastCallResult = ApiResponseResult.None;
            HostUrl = DevelopmentEnvironment ? Core.settings.Api.api_Dev_Host : Core.settings.Api.api_Host;
        }
    }
    public class ApiActions
    {
        public const string DEFAULT = "default_action";
    }
}