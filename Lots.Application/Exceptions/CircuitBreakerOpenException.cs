using System;

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException() 
        : base("Circuit breaker is open due to recent failures.") 
    { }

    public CircuitBreakerOpenException(string message) 
        : base(message) 
    { }

    public CircuitBreakerOpenException(string message, Exception innerException) 
        : base(message, innerException) 
    { }
}
