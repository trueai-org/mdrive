using Microsoft.AspNetCore.Mvc;
using RestSharp;
using System;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MDriveSync.Client.API
{
    //[ApiController]
    //[Route("[controller]")]
    //public class WeatherForecastController : ControllerBase

    [Route("/aliyundrive/api/open")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        // GET: api/<ValuesController>
        [HttpGet]
        public IEnumerable<string> Get()
        {

            // 授权链接拼接
            // https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf650xxx&redirect_uri=oob&scope=user:base,file:all:read

            // https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf6504bea8a611932684c86f6&redirect_uri=https://api.duplicati.net/api/open/aliyundrive&scope=user:base,file:all:read&relogin=true

            // 回调授权码
            // https://circle.ac.cn/aliyundrive/api/open?code=e5457eda87c54953938bab34305a3630

            //var code = Request.Query["code"];
            var api = "https://openapi.alipan.com/oauth/access_token";


            var client = new RestClient();
            var request = new RestRequest("https://openapi.alipan.com/oauth/access_token", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            var body = @"{
" + "\n" +
            @"    ""client_id"": ""12561ebaf6504bea8a611xxx"",
" + "\n" +
            @"    ""client_secret"": ""a0487f0bd5xxxxxx"",
" + "\n" +
            @"    ""grant_type"": ""authorization_code"",
" + "\n" +
            @"    ""code"": ""db7b6df60a274b9191cxxx""
" + "\n" +
            @"}";
            request.AddStringBody(body, DataFormat.Json);
            RestResponse response = client.ExecuteAsync(request).Result;
            Console.WriteLine(response.Content);


            // https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf6504bea8a61xxx6&redirect_uri=https://circle.ac.cn/aliyundrive/api/open&scope=user:base,file:all:read,file:all:write
            return new string[] { "value1", "value2" };
        }

        // GET api/<ValuesController>/5
        [HttpGet("{id}")]
        public async Task<string> Get(int id)
        {

            var api = "https://cn-beijing-data.aliyundrive.net/0Od6AwSe%2F51259322%2F6540dd6bea9515d775e145c1af0887575aa67206%2F6540dd6b7e223fe17954422294785402e20691a7?partNumber=1&security-token=CAIS%2BgF1q6Ft5B2yfSjIr5fQKNXduYVphPqcaGSAsHMtaLlJnKbC2zz2IHFPeHJrBeAYt%2FoxmW1X5vwSlq5rR4QAXlDfNTbAThSHqFHPWZHInuDox55m4cTXNAr%2BIhr%2F29CoEIedZdjBe%2FCrRknZnytou9XTfimjWFrXWv%2Fgy%2BQQDLItUxK%2FcCBNCfpPOwJms7V6D3bKMuu3OROY6Qi5TmgQ41Uh1jgjtPzkkpfFtkGF1GeXkLFF%2B97DRbG%2FdNRpMZtFVNO44fd7bKKp0lQLukMWr%2Fwq3PIdp2ma447NWQlLnzyCMvvJ9OVDFyN0aKEnH7J%2Bq%2FzxhTPrMnpkSlacGoABfK0%2By4o5omkQtAXt3cCxG8kQrxoGqG7%2FQiMG7YPZctTUTWfvg6SpgfWz0ccaBPEdpU1MagOyGz8JmH09HdOMqd9MA3l7kaw7vCTND6X9ReYCW0G5kKe4nSoADV59UQsH8ALxaUCmmcNFTB48%2BJOntz29qSsSkZegwyyTV4IGs88gAA%3D%3D&uploadId=E6405DD4EB7A41DD990822E77E311128&x-oss-access-key-id=STS.NTecoiTZH38wjB1Tsxd6esci9&x-oss-expires=1698753403&x-oss-signature=oZVppdmK5jRGAJWQ0iDOL9oXijrec6YsSdSoYQOZvCk%3D&x-oss-signature-version=OSS2";
            //var client = new RestClient();
            //var request = new RestRequest(api, Method.Put);

            //// 读取文件为字节数组
            //byte[] fileBytes = System.IO.File.ReadAllBytes(@"E:\downs\fdm\audition-gray.png");

            //// 设置请求主体为二进制数据
            //request.AddParameter("application/octet-stream", fileBytes, ParameterType.RequestBody);


            //// 执行请求
            //var response = client.Execute(request);

            //// 输出响应内容，可按需处理
            //Console.WriteLine(response.Content);

            string filePath = @"E:\downs\fdm\audition-gray.png";

            using (HttpClient httpClient = new HttpClient())
            {
                // 读取文件作为字节流
                byte[] fileData = await System.IO.File.ReadAllBytesAsync(filePath);

                // 创建HttpContent
                ByteArrayContent content = new ByteArrayContent(fileData);

                // 发送PUT请求
                HttpResponseMessage response = await httpClient.PutAsync(api, content);

                // 检查请求是否成功
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Successfully uploaded the file.");
                }
                else
                {
                    Console.WriteLine($"Failed to upload the file. Status Code: {response.StatusCode}");
                }
            }

            return "value";
        }

        // POST api/<ValuesController>
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<ValuesController>/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<ValuesController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
