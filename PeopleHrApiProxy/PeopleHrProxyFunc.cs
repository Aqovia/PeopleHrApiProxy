using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

namespace PeopleHrApiProxy
{
    public static class PeopleHrProxyFunc
    {
        [FunctionName("people-hr-proxy")]
        public static async Task<string> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "{path?}")] HttpRequest req, string path,
            ILogger log)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "No path supplied to a People HR API Resource.";

            var proxyResponse = await "https://api.peoplehr.net"
                .AppendPathSegment(path)
                .PostJsonAsync(req.GetQueryParameterDictionary());

            var statusCode = proxyResponse.StatusCode;
            var statusReason = proxyResponse.Headers.FirstOrDefault("Reason-Phrase");

            foreach (var (name, value) in proxyResponse.Headers)
            {
                if (!req.Headers.ContainsKey(name) && name != "transfer-encoding")
                    req.Headers.Add(new KeyValuePair<string, StringValues>(name, value));
            }

            var payloadRaw = await proxyResponse.GetStringAsync();

            dynamic payload = JsonConvert.DeserializeObject(payloadRaw);

            int payloadStatus;
            if (int.TryParse(payload.Status.ToString(), out payloadStatus))
            {
                switch (payloadStatus)
                {
                    case 0:
                        statusCode = StatusCodes.Status200OK;
                        break;
                    case 1:
                    case 2:
                        statusCode = StatusCodes.Status401Unauthorized;
                        break;
                    case 6:
                        statusCode = StatusCodes.Status502BadGateway;
                        break;
                    case 10:
                        statusCode = StatusCodes.Status404NotFound;
                        break;
                    default:
                        statusCode = StatusCodes.Status400BadRequest;
                        break;
                }
                if (payload.Message != null)
                    statusReason = payload.Message;
            }

            req.HttpContext.Response.StatusCode = statusCode;
            req.HttpContext.Response.Headers.Add("Reason-Phrase", statusReason);
            return payload.isError != true ? JsonConvert.SerializeObject(payload.Result) : payload.Message;
        }
    }
}
