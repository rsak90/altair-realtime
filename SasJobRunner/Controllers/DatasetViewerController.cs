using Microsoft.AspNetCore.Mvc;
using SasJobRunner.ViewModels;

namespace SasJobRunner.Controllers;

/// <summary>
/// Controller for the standalone dataset viewer page
/// </summary>
public class DatasetViewerController : Controller
{
    [HttpGet("/dataset-viewer/{datasetName}")]
    public IActionResult Index(string datasetName)
    {
        var sessionId = HttpContext.Session.GetString("SessionId");
        var userId = HttpContext.Session.GetString("UserId");

        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Index", "Auth");
        }

        var model = new DatasetViewModel
        {
            SessionId = sessionId,
            DatasetName = datasetName
        };

        return View(model);
    }
}
