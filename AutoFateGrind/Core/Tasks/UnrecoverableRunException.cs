using System;

namespace AutoFateGrind.Core.Tasks;

internal sealed class UnrecoverableRunException(string message) : Exception(message);
