using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Services;
using SasJobRunner.ViewModels;

namespace SasJobRunner.Controllers;

public class HistoryController(IProgramHistoryStore historyStore) : Controller
{
    [HttpGet("/History")]
    public async Task<IActionResult> Index()
    {
        var userId = HttpContext.Session.GetString("UserId") ?? string.Empty;

        var records = await historyStore.GetByUserAsync(userId);
        var items = records
            .Select(HistoryItemViewModel.FromRecord)
            .ToList();

        var model = new HistoryViewModel { Items = items };

        return View(model);
    }
}
