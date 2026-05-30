#ifndef NATIVE_UMM_BRIDGING_HEADER_H
#define NATIVE_UMM_BRIDGING_HEADER_H

// C entry points exported by libNativeUmm.a (NativeAOT-compiled C# installer).
// umm_run takes a request JSON string and returns a malloc'd response JSON
// string that the caller must release with umm_free.
char *umm_run(const char *requestJson);
void umm_free(char *response);

#endif
