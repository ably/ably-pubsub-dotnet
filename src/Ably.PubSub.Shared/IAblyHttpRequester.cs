using System.Threading.Tasks;

namespace Ably.PubSub
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "StyleCop.CSharp.DocumentationRules",
        "SA1600:Elements should be documented",
        Justification = "Internal interface.")]
    internal interface IAblyHttpRequester
    {
        Task<AblyResponse> Execute(AblyRequest request);
    }
}
