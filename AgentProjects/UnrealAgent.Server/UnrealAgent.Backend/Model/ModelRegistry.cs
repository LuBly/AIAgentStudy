using System.Reflection;
using UnrealAgent.Backend.Model.Attributes;

namespace UnrealAgent.Backend.Model;
/// <summary>
/// 어셈블리에서 [AgentModel] 어트리뷰트가 붙은 클래스를 스캔하여 모델 목록을 관리합니다.
/// </summary>
public sealed class ModelRegistry
{
    /// <summary> 전체 모델 배열 </summary>
    private readonly List<IModel> Models = [];
    
    /// <summary> 현재 ( 비레거시 ) 모델 목록 </summary>
    public IReadOnlyList<IModel> CurrentModels => Models;
    
    /// <summary> Id로 모델을 찾는다. </summary>
    public IModel? FindById(string Id) => Models.FirstOrDefault(m => m.Id == Id);

    public void DiscoverModels(params Assembly[] assemblies)
    {
        List<(IModel Model, int Order)> Discovered = [];

        foreach (Assembly asm in assemblies)
        {
            foreach (Type type in asm.GetTypes())
            {
                AgentModelAttribute? attr = type.GetCustomAttribute<AgentModelAttribute>();
                if (attr is null) continue;

                if (!typeof(IModel).IsAssignableFrom(type)) continue;
                
                if(Activator.CreateInstance(type) is IModel model && !attr.bIsLegacy)
                    Discovered.Add((model, attr.Order));
            }
        }
        
        Discovered.Sort((A, B) => A.Order.CompareTo(B.Order));
        Models.AddRange(Discovered.Select(e => e.Model));
    }
}