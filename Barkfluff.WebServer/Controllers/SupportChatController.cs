using Barkfluff.WebServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace Barkfluff.WebServer.Controllers
{
    [ApiController]
    [Route("api/support")]
    public class SupportChatController : ControllerBase
    {
        private readonly SupportChatService _chatService;
        private readonly TelegramService _telegramService;

        public SupportChatController(SupportChatService chatService, TelegramService telegramService)
        {
            _chatService = chatService;
            _telegramService = telegramService;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ChatId) || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { error = "ChatId and Message are required" });

            if (!Guid.TryParse(request.ChatId, out _))
                return BadRequest(new { error = "Invalid ChatId format" });

            if (request.Message.Length > 4000)
                return BadRequest(new { error = "Message too long" });

            _chatService.AddMessage(request.ChatId, request.Message, isAdmin: false);

            try
            {
                await _telegramService.SendToAdmin(request.ChatId, request.Message);
            }
            catch (Exception)
            {
                // Message is still saved locally even if Telegram fails
            }

            return Ok(new { success = true });
        }

        [HttpGet("messages/{chatId}")]
        public IActionResult GetMessages(string chatId, [FromQuery] int after = 0)
        {
            if (!Guid.TryParse(chatId, out _))
                return BadRequest(new { error = "Invalid ChatId format" });

            var messages = _chatService.GetMessages(chatId, after);
            var total = _chatService.GetMessageCount(chatId);
            return Ok(new { messages, total });
        }
    }

    public class SendMessageRequest
    {
        public string ChatId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
