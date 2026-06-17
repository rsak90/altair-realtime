using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Models;
using SasJobRunner.Services;
using SasJobRunner.ViewModels;

namespace SasJobRunner.Controllers;

public class SessionController(
    IProgramHistoryStore historyStore, 
    IMacroVarStore macroVarStore,
    IConfiguration configuration,
    ILogger<SessionController> logger) : Controller
{
    [HttpGet("/Session")]
    public async Task<IActionResult> Index()
    {
        var userId = HttpContext.Session.GetString("UserId") ?? string.Empty;
        var activeSessionId = HttpContext.Session.GetString("SessionId");

        // Build past sessions from program history records
        var historyRecords = await historyStore.GetByUserAsync(userId);
        var pastSessions = historyRecords
            .GroupBy(r => r.SessionId)
            .Select(g =>
            {
                var first = g.OrderByDescending(r => r.SubmittedAt).First();
                return new SessionInfo(
                    UserId: userId,
                    SessionId: g.Key,
                    CreatedAt: g.Min(r => r.SubmittedAt),
                    LastAccessedAt: first.SubmittedAt);
            })
            .OrderByDescending(s => s.LastAccessedAt)
            .ToList();

        var model = new SessionViewModel
        {
            ActiveSessionId = activeSessionId,
            PastSessions = pastSessions
        };

        return View(model);
    }

    [HttpPost("/Session/New")]
    public IActionResult New()
    {
        var userId = HttpContext.Session.GetString("UserId") ?? string.Empty;
        var sessionId = SessionIdGenerator.Generate();

        try
        {
            // Use the configured StudyFolder to construct the correct path
            var studyFolder = configuration["SessionStorage:StudyFolder"];
            if (!string.IsNullOrEmpty(studyFolder))
            {
                var sessionPath = Path.Combine(studyFolder.TrimEnd('/'), "sessions", userId, sessionId);
                Directory.CreateDirectory(sessionPath);
                logger.LogInformation("Created new session directory: {SessionPath}", sessionPath);
            }
            else
            {
                logger.LogWarning("SessionStorage:StudyFolder not configured");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create session directory for UserId: {UserId}, SessionId: {SessionId}", userId, sessionId);
        }

        HttpContext.Session.SetString("SessionId", sessionId);

        return RedirectToAction("Index", "Editor");
    }

    [HttpPost("/Session/Resume")]
    public async Task<IActionResult> Resume([FromForm] string sessionId)
    {
        // Load macro vars for resumed session so they are ready in the store
        await macroVarStore.GetAsync(sessionId);

        HttpContext.Session.SetString("SessionId", sessionId);

        return RedirectToAction("Index", "Editor");
    }
}
