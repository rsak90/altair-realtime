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

        var model = new EditorViewModel
        {
            SessionId = sessionId,
            UserId = userId,
            InitialCode = string.Empty
        };

        return View(model);
    }
}
