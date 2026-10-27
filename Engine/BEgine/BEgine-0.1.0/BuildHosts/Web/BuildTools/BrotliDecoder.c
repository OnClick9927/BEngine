#include <stddef.h>
#include <stdint.h>

extern int BrotliDecoderDecompress(size_t encoded_size, const uint8_t* encoded_buffer,
                                  size_t* decoded_size, uint8_t* decoded_buffer);

// The caller allocates a bounded output buffer from the verified bundle index.
// Accept only a complete stream with exactly the expected decoded size.
int bengine_brotli_decode(const uint8_t* input, int input_size, uint8_t* output, int output_size)
{
    if (input_size < 0 || output_size < 0) return 0;
    size_t size = (size_t)output_size;
    return BrotliDecoderDecompress((size_t)input_size, input, &size, output) == 1 && size == (size_t)output_size;
}
