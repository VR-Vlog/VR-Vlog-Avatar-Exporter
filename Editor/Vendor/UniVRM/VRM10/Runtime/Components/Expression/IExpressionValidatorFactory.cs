namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    public interface IExpressionValidatorFactory
    {
        IExpressionValidator Create(VRM10ObjectExpression expressionAvatar);
    }
}