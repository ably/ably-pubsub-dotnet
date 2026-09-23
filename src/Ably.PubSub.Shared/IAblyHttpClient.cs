using System.Threading.Tasks;

namespace IO.Ably
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "StyleCop.CSharp.DocumentationRules",
        "SA1600:Elements should be documented",
        Justification = "Internal interface.")]
    internal interface IAblyHttpClient
    {
        Task<AblyResponse> Execute(AblyRequest request);
    }
}
