using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AswaqSallaAPI
{
    [ApiController]
    [Route("notifications")]
    public class SallaWebhookController : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            var body = await new StreamReader(Request.Body).ReadToEndAsync();
            Console.WriteLine("Webhook Received: " + body);
            return Ok();
        }
    }
}
