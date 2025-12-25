using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Centralized error handling and validation utilities.
    /// Provides consistent error reporting and recovery suggestions.
    /// </summary>
    public static class ErrorHandler {
        /// <summary>
        /// Logs an error with context and returns a user-friendly message.
        /// </summary>
        public static string HandleError(Exception exception, string context, string userMessage = null) {
            if (exception == null) {
                return "An unknown error occurred";
            }

            Log.Error(exception, context);

            if (!string.IsNullOrWhiteSpace(userMessage)) {
                return userMessage;
            }

            return GetUserFriendlyMessage(exception, context);
        }

        /// <summary>
        /// Gets a user-friendly error message based on the exception type.
        /// </summary>
        public static string GetUserFriendlyMessage(Exception exception, string context = null) {
            var message = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(context)) {
                message.AppendLine(context);
            }

            switch (exception) {
                case UnauthorizedAccessException _:
                    message.AppendLine("Access denied. Please check file permissions.");
                    message.AppendLine("Try running as administrator or check if files are in use.");
                    break;

                case System.IO.IOException ioEx when ioEx.Message.Contains("being used"):
                    message.AppendLine("File is being used by another process.");
                    message.AppendLine("Close any applications that might be using the file and try again.");
                    break;

                case System.IO.FileNotFoundException _:
                    message.AppendLine("File not found.");
                    message.AppendLine("The file may have been moved or deleted.");
                    break;

                case System.IO.DirectoryNotFoundException _:
                    message.AppendLine("Directory not found.");
                    message.AppendLine("The path may be incorrect or the directory was deleted.");
                    break;

                case System.IO.PathTooLongException _:
                    message.AppendLine("The path is too long.");
                    message.AppendLine("Try moving files to a location with a shorter path.");
                    break;

                case OutOfMemoryException _:
                    message.AppendLine("Out of memory.");
                    message.AppendLine("Close other applications or restart the program.");
                    break;

                case ArgumentNullException argEx:
                    message.AppendLine($"Invalid argument: {argEx.ParamName}");
                    message.AppendLine("This is likely a programming error. Please report this issue.");
                    break;

                case InvalidOperationException _:
                    message.AppendLine("Invalid operation.");
                    message.AppendLine(exception.Message);
                    break;

                default:
                    message.AppendLine("An unexpected error occurred:");
                    message.AppendLine(exception.Message);
                    break;
            }

            return message.ToString();
        }

        /// <summary>
        /// Validates a file path and returns error message if invalid.
        /// </summary>
        public static ValidationResult ValidateFilePath(string path, bool mustExist = true) {
            if (string.IsNullOrWhiteSpace(path)) {
                return ValidationResult.Fail("Path cannot be empty");
            }

            try {
                var fullPath = System.IO.Path.GetFullPath(path);
                
                if (mustExist && !System.IO.File.Exists(fullPath)) {
                    return ValidationResult.Fail($"File does not exist: {fullPath}");
                }

                return ValidationResult.Success();
            } catch (Exception e) {
                return ValidationResult.Fail($"Invalid path: {e.Message}");
            }
        }

        /// <summary>
        /// Validates a directory path and returns error message if invalid.
        /// </summary>
        public static ValidationResult ValidateDirectoryPath(string path, bool mustExist = true) {
            if (string.IsNullOrWhiteSpace(path)) {
                return ValidationResult.Fail("Path cannot be empty");
            }

            try {
                var fullPath = System.IO.Path.GetFullPath(path);
                
                if (mustExist && !System.IO.Directory.Exists(fullPath)) {
                    return ValidationResult.Fail($"Directory does not exist: {fullPath}");
                }

                return ValidationResult.Success();
            } catch (Exception e) {
                return ValidationResult.Fail($"Invalid path: {e.Message}");
            }
        }

        /// <summary>
        /// Safely executes an action with error handling and retry logic.
        /// </summary>
        public static OperationResult<T> TryExecute<T>(
            Func<T> action, 
            string operationName, 
            int maxRetries = 3, 
            int delayMs = 100) {
            
            Exception lastException = null;
            int attempts = 0;

            while (attempts < maxRetries) {
                attempts++;
                try {
                    var result = action();
                    return OperationResult<T>.Success(result);
                } catch (Exception e) {
                    lastException = e;
                    
                    if (attempts < maxRetries) {
                        Log.Warning(e, $"{operationName} failed (attempt {attempts}/{maxRetries}), retrying...");
                        System.Threading.Thread.Sleep(delayMs);
                    }
                }
            }

            var errorMessage = HandleError(lastException, 
                $"{operationName} failed after {maxRetries} attempts");
            return OperationResult<T>.Fail(errorMessage, lastException);
        }

        /// <summary>
        /// Safely executes an action with error handling.
        /// </summary>
        public static OperationResult TryExecute(
            Action action, 
            string operationName, 
            int maxRetries = 3, 
            int delayMs = 100) {
            
            return TryExecute(() => {
                action();
                return true;
            }, operationName, maxRetries, delayMs);
        }
    }

    /// <summary>
    /// Result of a validation operation.
    /// </summary>
    public class ValidationResult {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();

        public static ValidationResult Success() {
            return new ValidationResult { IsValid = true };
        }

        public static ValidationResult Fail(string errorMessage) {
            return new ValidationResult { 
                IsValid = false, 
                ErrorMessage = errorMessage 
            };
        }

        public ValidationResult WithWarning(string warning) {
            Warnings.Add(warning);
            return this;
        }
    }

    /// <summary>
    /// Result of an operation with optional return value.
    /// </summary>
    public class OperationResult<T> {
        public bool Success { get; set; }
        public T Value { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }

        public static OperationResult<T> Success(T value) {
            return new OperationResult<T> { 
                Success = true, 
                Value = value 
            };
        }

        public static OperationResult<T> Fail(string errorMessage, Exception exception = null) {
            return new OperationResult<T> { 
                Success = false, 
                ErrorMessage = errorMessage,
                Exception = exception
            };
        }
    }

    /// <summary>
    /// Result of an operation without return value.
    /// </summary>
    public class OperationResult : OperationResult<bool> {
        public new static OperationResult Success() {
            return new OperationResult { 
                Success = true, 
                Value = true 
            };
        }

        public new static OperationResult Fail(string errorMessage, Exception exception = null) {
            return new OperationResult { 
                Success = false, 
                ErrorMessage = errorMessage,
                Exception = exception
            };
        }
    }
}
