using Microsoft.AspNetCore.Mvc;
using SasJobRunner.ViewModels;

namespace SasJobRunner.Controllers;

public class EditorController : Controller
{
    [HttpGet("/Editor")]
    public IActionResult Index()
    {
        var sessionId = HttpContext.Session.GetString("SessionId");
        var userId = HttpContext.Session.GetString("UserId");

        // If user doesn't have an active session, create one automatically
        if (string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(userId))
        {
            sessionId = Guid.NewGuid().ToString();
            Directory.CreateDirectory($"/sas/sessions/{userId}/{sessionId}/");
            HttpContext.Session.SetString("SessionId", sessionId);
        }

        var model = new EditorViewModel
        {
            SessionId = sessionId,
            UserId = userId,
            InitialCode = string.Empty
        };

        return View(model);
    }
}
