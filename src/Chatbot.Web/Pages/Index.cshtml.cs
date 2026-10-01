using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Chatbot.Web.Pages;

public class IndexModel : PageModel
{
    public IndexModel(IOptions<FrontendOptions> options) => ApiBaseUrl = options.Value.ApiBaseUrl.TrimEnd('/');

    public string ApiBaseUrl { get; }

    public void OnGet()
    {
    }
}
