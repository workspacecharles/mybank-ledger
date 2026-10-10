namespace MyBank.LoadTests;

internal enum Outcome
{
    Success = 1,
    BusinessRejection = 2,
    UnexpectedClientError = 3,
    ServerError = 4,
    Timeout = 5,
    TransportError = 6,
}
