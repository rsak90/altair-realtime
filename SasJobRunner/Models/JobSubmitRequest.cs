using System.ComponentModel.DataAnnotations;

namespace SasJobRunner.Models;

public record JobSubmitRequest(
    [Required] string SessionId,
    [Required] string SourceCode
);
