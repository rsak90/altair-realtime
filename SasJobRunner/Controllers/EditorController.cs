using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Services;
using SasJobRunner.ViewModels;

namespace SasJobRunner.Controllers;

public class EditorController(IConfiguration configuration, ILogger<EditorController> logger) : Controller
{
    [HttpGet("/Editor")]
    public IActionResult Index()
    {
        var sessionId = HttpContext.Session.GetString("SessionId");
        var userId = HttpContext.Session.GetString("UserId");

        logger.LogInformation("Editor Index accessed - UserId: {UserId}, SessionId: {SessionId}", 
            userId ?? "(null)", sessionId ?? "(null)");

        // If user doesn't have an active session, create one automatically
        if (string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(userId))
        {
            sessionId = SessionIdGenerator.Generate();
            
            try
            {
                // Use the configured StudyFolder to construct the correct path
                var studyFolder = configuration["SessionStorage:StudyFolder"];
                if (!string.IsNullOrEmpty(studyFolder))
                {
                    var sessionPath = Path.Combine(studyFolder.TrimEnd('/'), "sessions", userId, sessionId);
                    Directory.CreateDirectory(sessionPath);
                    logger.LogInformation("Created session directory: {SessionPath}", sessionPath);
                }
                else
                {
                    logger.LogWarning("SessionStorage:StudyFolder not configured, session directory not created");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create session directory for UserId: {UserId}, SessionId: {SessionId}", userId, sessionId);
            }

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
