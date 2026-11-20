#include <stdlib.h>
#ifndef FIXTURE_ABI
#define FIXTURE_ABI 1
#endif
#define API __declspec(dllexport)
typedef struct { int channels; float gain, memory; } State;
static int live, created, destroyed;
API int bengine_dsp_abi_version(void) { return FIXTURE_ABI; }
API void* bengine_dsp_create(int rate, int channels)
{
    State* state;
    if(rate==96000) return NULL;
    state=(State*)calloc(1,sizeof(State));
    if(!state) return NULL;
    state->channels=channels; state->gain=1; live++; created++; return state;
}
API void bengine_dsp_destroy(void* pointer) { live--; destroyed++; free(pointer); }
API void bengine_dsp_set_parameter(void* pointer, int index, float value)
{ if(index==0) ((State*)pointer)->gain=value; }
#ifndef FIXTURE_MISSING_PROCESS
API void bengine_dsp_process(void* pointer, float* samples, int frames)
{
    State* state=(State*)pointer; int i;
    for(i=0;i<frames*state->channels;i++) { samples[i]=samples[i]*state->gain+state->memory; state->memory+=0.125f; }
}
#endif
API int bengine_dsp_has_tail(void* pointer) { return ((State*)pointer)->memory!=0; }
API int fixture_live(void) { return live; }
API int fixture_created(void) { return created; }
API int fixture_destroyed(void) { return destroyed; }
// Neutral-module consumer, unrelated to the audio ABI.
API int fixture_add(int a, int b) { return a+b; }
