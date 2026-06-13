using Microsoft.AspNetCore.Mvc;
using SasJobRunner.ViewModels;

namespace SasJobRunner.Controllers;

public class DatasetController : Controller
{
    [HttpGet("/Dataset")]
    public IActionResult Index()
    {
        var sessionId = HttpContext.Session.GetString("SessionId");

        var model = new DatasetViewModel
        {
            SessionId = sessionId
        };

        return View(model);
    }
}
