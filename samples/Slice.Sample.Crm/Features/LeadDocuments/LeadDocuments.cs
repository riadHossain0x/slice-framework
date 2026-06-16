using System.Text;
using Microsoft.AspNetCore.Mvc;
using Slice.AspNetCore.Mvc;
using Slice.BlobStoring;

namespace Slice.Sample.Crm.Features.LeadDocuments;

/// <summary>Blob container marker for lead documents.</summary>
public sealed class LeadDocumentContainer;

// Minimal round-trip demo of IBlobContainer (anonymous for easy verification).
[Route("api/crm/lead-documents")]
public sealed class LeadDocumentsController(IBlobContainer<LeadDocumentContainer> container) : SliceController
{
    [HttpPut("{name}")]
    public async Task<IActionResult> Put(string name, [FromBody] string content, CancellationToken ct)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await container.SaveAsync(name, stream, overrideExisting: true, ct);
        return Ok(new { saved = name });
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> Get(string name, CancellationToken ct)
    {
        await using var stream = await container.GetOrNullAsync(name, ct);
        if (stream is null) return NotFound();
        using var reader = new StreamReader(stream);
        return Ok(new { name, content = await reader.ReadToEndAsync(ct) });
    }
}
