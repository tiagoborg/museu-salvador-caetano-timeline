namespace EnigmaVirtual.OneCoreClient.Model
{
    [System.Serializable]
    public class ResponseBase
    {
        public string[] errors;
        public object data;

        internal bool success => errors.IsNullOrEmpty();
    }
}