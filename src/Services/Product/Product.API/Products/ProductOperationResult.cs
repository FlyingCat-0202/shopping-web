namespace Product.API.Products;

internal sealed record ProductOperationResult<T>(
    ProductOperationStatus Status,
    T? Payload,
    string? Message)
{
    public static ProductOperationResult<T> Ok(T payload)
        => new(ProductOperationStatus.Ok, payload, null);

    public static ProductOperationResult<T> Created(T payload)
        => new(ProductOperationStatus.Created, payload, null);

    public static ProductOperationResult<T> Accepted(T payload, string message)
        => new(ProductOperationStatus.Accepted, payload, message);

    public static ProductOperationResult<T> BadRequest(string message)
        => new(ProductOperationStatus.BadRequest, default, message);

    public static ProductOperationResult<T> NotFound(string message)
        => new(ProductOperationStatus.NotFound, default, message);

    public static ProductOperationResult<T> Conflict(string message)
        => new(ProductOperationStatus.Conflict, default, message);
}
