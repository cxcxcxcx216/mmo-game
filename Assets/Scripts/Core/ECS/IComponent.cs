namespace Minecraft.Core.ECS
{
    /// <summary>
    /// ECS 组件接口。组件是纯数据载体，不包含任何逻辑。
    /// 使用 class 实现（而非 struct），因为需要通过 World 的 Dictionary 存储，
    /// 并支持多态查询。组件应保持精简，只存储数据。
    /// </summary>
    public interface IComponent
    {
    }
}
