namespace EnigmaVirtual.OneCoreClient
{
    [System.Serializable]
    public class SignInResponse : Model.ResponseBase
    {
        new public SignInResponseData data;

        [System.Serializable]
        public class SignInResponseData
        {
            public string[] permissions;
        }
    }    
}