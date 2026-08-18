namespace AIBT
{
#if UNITY_INCLUDE_TESTS
    internal static class NativeCommandAsyncTestHooksV1
    {
        private static int s_failureOrdinal;
        private static int s_allocationOrdinal;

        internal static void FailAllocationAt(int ordinal)
        {
            s_failureOrdinal = ordinal;
            s_allocationOrdinal = 0;
        }

        internal static void ResetAllocationFailure()
        {
            s_failureOrdinal = 0;
            s_allocationOrdinal = 0;
        }

        internal static void BeforeAllocation()
        {
            s_allocationOrdinal++;
            if (s_failureOrdinal != 0 && s_allocationOrdinal == s_failureOrdinal)
            {
                throw new NativeCommandAsyncInjectedAllocationExceptionV1();
            }
        }
    }

    internal sealed class NativeCommandAsyncInjectedAllocationExceptionV1 : System.Exception
    {
    }
#endif
}
