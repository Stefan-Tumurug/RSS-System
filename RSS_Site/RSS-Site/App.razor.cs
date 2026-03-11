using Microsoft.AspNetCore.Components;
using RssSite.Components.Authentication;

namespace RssSite
{
    public partial class App : ComponentBase
    {
        [Inject]
        private AuthService AuthService { get; set; } = default!;
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await AuthService.IsAuthenticatedAsync();
            StateHasChanged();

        }
    }
}
