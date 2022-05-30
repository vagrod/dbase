namespace Dbase;

public struct ErrorOr<T>
{
    public T? Value { get; private set; }
    public string? Error { get; private set; }
    
    public bool Failed => Error != null;
    
    public static ErrorOr<T> Success(T value)
    {
        return new ErrorOr<T>
        {
            Value = value
        };
    }
    
    public static ErrorOr<T> Fail(string error)
    {
        return new ErrorOr<T>
        {
            Error = error
        };
    }

    public T Unwrap()
    {
        if(Value != null)
            return Value;

        throw new Exception("Trying to unwrap a failed ErrorOr");
    }
    
    public string UnwrapError()
    {
        return Error ?? throw new Exception("Trying to unwrap an empty Error");
    }
    
}

public struct SuccessOr<T>
{
    public bool IsSuccess { get; private set; }
    public T? Error { get; private set; }
    public bool Failed => Error != null;
    
    public T UnwrapError()
    {
        return Error ?? throw new Exception("Trying to unwrap an empty Error");
    }
    
    public static SuccessOr<T> Success =>
        new()
        {
            IsSuccess = true
        };
    
    public static SuccessOr<T> Fail(T errorValue)
    {
        return new SuccessOr<T>
        {
            Error = errorValue
        };
    }
    
}

public struct Result
{
    public bool IsSuccess { get; private set; }
    public bool Failed => !IsSuccess;
    
    public string? Error { get; private set; }

    public string UnwrapError() {
        return Error ?? throw new Exception("Trying to unwrap an empty Error");
    }
    
    public static Result Success =>
      new Result
        {
            IsSuccess = true
        };
    
    public static Result Fail(string error)
    {
        return new Result
        {
            Error = error
        };
    }
    
}
