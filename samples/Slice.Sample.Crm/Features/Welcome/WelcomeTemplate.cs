using Microsoft.AspNetCore.Mvc;
using Slice.AspNetCore.Mvc;
using Slice.VirtualFileSystem;

namespace Slice.Sample.Crm.Features.Welcome;

// Reads an embedded resource through the virtual file system (anonymous, for verification).
[Route("api/crm/welcome-template")]
public sealed class WelcomeTemplateController(IVirtualFileProvider vfs) : SliceController
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var content = await vfs.ReadAsStringAsync("Resources/welcome.txt");
        return content is null ? NotFound() : Ok(new { content });
    }
}
