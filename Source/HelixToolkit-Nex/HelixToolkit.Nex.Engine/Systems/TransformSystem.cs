namespace HelixToolkit.Nex.Engine.Systems
{
    public sealed class TransformSystem : System
    {
        public override string Name => nameof(TransformSystem);

        protected override ResultCode OnInitializing()
        {
            return ResultCode.Ok;
        }

        protected override ResultCode OnTearingDown()
        {
            return ResultCode.Ok;
        }

        public override void Update(SystemContext context, float deltaTime)
        {
            var world = context.DataProvider.World;
        }
    }
}
