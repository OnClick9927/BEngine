/* BEngine native DSP ABI v1. Export these symbols with C linkage and cdecl.
   samples contains frames*channels interleaved float32 values, modified in place.
   Each create call owns independent state. Callbacks must not throw across the ABI.
   The host keeps the library loaded until registration and all instances are released. */
#pragma once
#ifdef __cplusplus
extern "C" {
#endif
int bengine_dsp_abi_version(void); /* must return 1 */
void* bengine_dsp_create(int sample_rate, int channels); /* NULL rejects the format */
void bengine_dsp_destroy(void* state);
void bengine_dsp_set_parameter(void* state, int index, float value);
void bengine_dsp_process(void* state, float* samples, int frames);
int bengine_dsp_has_tail(void* state);
#ifdef __cplusplus
}
#endif
