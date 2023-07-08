namespace ActorMicroservice.Common
{
    public class ServiceInformation
    {
        public ServiceInformation()
        {
        }

        public ServiceInformation(string serviceName, string serviceNameId, string urlSegment)
        {
            ServiceName = serviceName;
            ServiceNameId = serviceNameId;
            UrlSegment = urlSegment;
        }
        public string ServiceName { get; set; }
        public string ServiceNameId { get; set; }
        public string UrlSegment { get; set; }
    }
}
