using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using System.Web;
using System.Threading.Tasks;

namespace Luis
{
    public class LuisResponse
    {
        public Intentsresult[] intents { get; set; }
        public EntitiiesResult[] entities { get; set; }
        //public string utteranceText { get; set; }
        //public string[] tokenizedText { get; set; }
        //public string exampleId { get; set; }
        //public object metadata { get; set; }
    }

    public class Intentsresult
    {
        public string intent { get; set; }
        public float score { get; set; }
    }

    public class EntitiiesResult
    {
        public string entity { get; set; }
        public string type { get; set; }
        public float score { get; set; }
    }



    public class Luis
    {
        private string subscriptionKey; // = "9ce468352b634d198cc288e2d6aa3581";
        private string applicationId;   // = "330d4213-c738-4df2-8cf8-90a1f4992d4d";
        private string baseUri = "https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/";

        public Luis(string _subscriptionKey, string _applicationId)
        {
            subscriptionKey = _subscriptionKey;
            applicationId = _applicationId;
        }

        public async Task<LuisResponse> GetIntent(string message)
        {
            HttpClient c = new HttpClient();
            //c.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);

            string headUrl = baseUri + string.Format("{0}?subscription-key={1}&q=", applicationId, subscriptionKey);
            string fullUrl = headUrl + WebUtility.UrlEncode(message);
            

            string responseBody = await c.GetStringAsync(fullUrl);
            LuisResponse response = JsonConvert.DeserializeObject<LuisResponse>(responseBody);
            return response;
        }
    }
}
