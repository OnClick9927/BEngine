// Thin synchronous FreeType boundary. HarfBuzz owns shaping in the managed UI
// backend; this file only supplies font metrics, coverage and positioned glyphs.
#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_TRUETYPE_TABLES_H
#include FT_COLOR_H
#include <math.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

typedef struct {
    FT_Library library;
    FT_Face face;
    uint8_t *data;
    float bitmap_scale;
    float last_size;
} BEngineFont;

void bengine_font_destroy(void *handle) {
    BEngineFont *font = handle;
    if (!font) return;
    if (font->face) FT_Done_Face(font->face);
    if (font->library) FT_Done_FreeType(font->library);
    free(font->data);
    free(font);
}

void *bengine_font_create(const uint8_t *data, int length) {
    if (!data || length <= 0 || length > 128 * 1024 * 1024) return NULL;
    BEngineFont *font = calloc(1, sizeof(*font));
    if (!font) return NULL;
    font->data = malloc((size_t)length);
    if (!font->data) { bengine_font_destroy(font); return NULL; }
    memcpy(font->data, data, (size_t)length);
    if (FT_Init_FreeType(&font->library) ||
        FT_New_Memory_Face(font->library, font->data, length, 0, &font->face)) {
        bengine_font_destroy(font); return NULL;
    }
    // Some pinned FreeType builds expose the COLRv1 API but cannot interpret
    // its paint graph. Reject the table format itself before any outline can
    // silently replace color glyphs. This also protects runtime-loaded fonts
    // which did not pass the content builder's capability checks.
    FT_Byte color_version[2];
    FT_ULong color_length = sizeof(color_version), svg_length = 0;
    if ((!FT_Load_Sfnt_Table(font->face, FT_MAKE_TAG('C', 'O', 'L', 'R'), 0,
            color_version, &color_length) &&
            (color_length != 2 || color_version[0] != 0 || color_version[1] != 0)) ||
        (!FT_Load_Sfnt_Table(font->face, FT_MAKE_TAG('S', 'V', 'G', ' '), 0,
            NULL, &svg_length) && svg_length != 0)) {
        bengine_font_destroy(font); return NULL;
    }
    if (FT_Select_Charmap(font->face, FT_ENCODING_UNICODE) &&
        FT_Select_Charmap(font->face, FT_ENCODING_MS_SYMBOL) && !font->face->charmap) {
        bengine_font_destroy(font); return NULL;
    }
    font->bitmap_scale = 1;
    return font;
}

static int set_size(BEngineFont *font, float size) {
    if (!font || !isfinite(size) || size < 1 || size > 512) return -1;
    if (font->last_size == size) return 0;
    font->bitmap_scale = 1;
    if (FT_IS_SCALABLE(font->face)) {
        if (FT_Set_Char_Size(font->face, 0, (FT_F26Dot6)lroundf(size * 64), 72, 72)) return -2;
        font->last_size = size;
        return 0;
    }
    if (font->face->num_fixed_sizes <= 0) return -2;
    int best = 0;
    float distance = INFINITY;
    for (int index = 0; index < font->face->num_fixed_sizes; index++) {
        float pixels = font->face->available_sizes[index].y_ppem / 64.0f;
        float candidate = fabsf(size - pixels);
        if (pixels > 0 && candidate < distance) { best = index; distance = candidate; }
    }
    if (FT_Select_Size(font->face, best)) return -2;
    float pixels = font->face->available_sizes[best].y_ppem / 64.0f;
    if (!(pixels > 0)) return -2;
    font->bitmap_scale = size / pixels;
    font->last_size = size;
    return 0;
}

int bengine_font_metrics(void *handle, float size, float *metrics) {
    BEngineFont *font = handle;
    if (!metrics || set_size(font, size)) return -1;
    FT_Face face = font->face;
    float ascent, descent, leading;
    if (face->units_per_EM) {
        float units = face->units_per_EM;
        TT_OS2 *os2 = FT_Get_Sfnt_Table(face, FT_SFNT_OS2);
        if (os2 && os2->version != 0xffff && (os2->fsSelection & (1u << 7))) {
            ascent = -(float)os2->sTypoAscender / units * size;
            descent = -(float)os2->sTypoDescender / units * size;
            leading = (float)os2->sTypoLineGap / units * size;
        } else {
            ascent = -(float)face->ascender / units * size;
            descent = -(float)face->descender / units * size;
            leading = (float)(face->height + face->descender - face->ascender) / units * size;
        }
    } else {
        ascent = -face->size->metrics.ascender / 64.0f * font->bitmap_scale;
        descent = -face->size->metrics.descender / 64.0f * font->bitmap_scale;
        float height = face->size->metrics.height / 64.0f * font->bitmap_scale;
        leading = height - (descent - ascent);
    }
    metrics[0] = ascent;
    metrics[1] = descent;
    metrics[2] = fmaxf(0, leading);
    return 0;
}

uint32_t bengine_font_glyph(void *handle, uint32_t codepoint) {
    BEngineFont *font = handle;
    if (!font || codepoint > 0x10ffff) return 0;
    // Match the existing Skia coverage contract; FreeType aliases low bytes to
    // the symbol PUA range, but that would suppress the correct fallback face.
    if (codepoint < 256 && font->face->charmap->encoding == FT_ENCODING_MS_SYMBOL) return 0;
    return FT_Get_Char_Index(font->face, codepoint);
}

static unsigned multiply(unsigned value, unsigned alpha) {
    return (value * alpha + 127) / 255;
}

static void source_over(uint8_t *pixel, unsigned red, unsigned green, unsigned blue, unsigned alpha) {
    unsigned inverse = 255 - alpha;
    pixel[0] = (uint8_t)(red + multiply(pixel[0], inverse));
    pixel[1] = (uint8_t)(green + multiply(pixel[1], inverse));
    pixel[2] = (uint8_t)(blue + multiply(pixel[2], inverse));
    pixel[3] = (uint8_t)(alpha + multiply(pixel[3], inverse));
}

static int bitmap_pixel(const FT_Bitmap *bitmap, int x, int y, unsigned *rgba) {
    if (x < 0 || y < 0 || x >= (int)bitmap->width || y >= (int)bitmap->rows) {
        memset(rgba, 0, sizeof(unsigned) * 4); return 0;
    }
    const uint8_t *row = bitmap->buffer + (bitmap->pitch >= 0 ?
        y * bitmap->pitch : ((int)bitmap->rows - 1 - y) * -bitmap->pitch);
    unsigned alpha;
    switch (bitmap->pixel_mode) {
        case FT_PIXEL_MODE_BGRA:
            rgba[0] = row[x * 4 + 2]; rgba[1] = row[x * 4 + 1];
            rgba[2] = row[x * 4]; rgba[3] = row[x * 4 + 3]; return 1;
        case FT_PIXEL_MODE_GRAY:
            alpha = bitmap->num_grays > 1 ? row[x] * 255u / (bitmap->num_grays - 1) : 0; break;
        case FT_PIXEL_MODE_MONO: alpha = (row[x / 8] & (0x80u >> (x % 8))) ? 255 : 0; break;
        case FT_PIXEL_MODE_GRAY2: alpha = ((row[x / 4] >> (6 - 2 * (x % 4))) & 3) * 85; break;
        case FT_PIXEL_MODE_GRAY4: alpha = ((row[x / 2] >> (4 - 4 * (x % 2))) & 15) * 17; break;
        default: return -1;
    }
    rgba[0] = rgba[1] = rgba[2] = rgba[3] = alpha;
    return 0;
}

// rgba is 0xRRGGBBAA; destination is caller-owned premultiplied RGBA8.
// Negative results are explicit unsupported/corrupt glyph errors, never fallback
// outlines for unsupported COLRv1 or SVG color paint graphs.
int bengine_font_draw_glyph(void *handle, uint32_t glyph, float size, float x, float baseline,
    uint32_t rgba, uint8_t *destination, int width, int height) {
    BEngineFont *font = handle;
    if (!destination || width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
        !isfinite(x) || !isfinite(baseline) || fabsf(x) > 1048576 || fabsf(baseline) > 1048576 ||
        set_size(font, size)) return -1;
    if (glyph >= (uint32_t)font->face->num_glyphs) return -3;
    FT_OpaquePaint paint;
    if (FT_Get_Color_Glyph_Paint(font->face, glyph, FT_COLOR_INCLUDE_ROOT_TRANSFORM, &paint)) return -10;
    int origin_x = (int)floorf(x), origin_y = (int)floorf(baseline);
    FT_Vector delta = { (FT_Pos)lroundf((x - origin_x) * 64),
        (FT_Pos)-lroundf((baseline - origin_y) * 64) };
    FT_Set_Transform(font->face, NULL, &delta);
    FT_Color foreground_color = { (uint8_t)(rgba >> 8), (uint8_t)(rgba >> 16),
        (uint8_t)(rgba >> 24), 255 };
    // Palette entry 0xffff uses the text foreground. Apply the caller's alpha
    // once, during compositing, including ordinary colored bitmap glyphs.
    FT_Palette_Set_Foreground_Color(font->face, foreground_color);
    if (FT_Load_Glyph(font->face, glyph, FT_LOAD_DEFAULT | FT_LOAD_COLOR)) return -4;
    FT_GlyphSlot slot = font->face->glyph;
    if (slot->format == FT_GLYPH_FORMAT_SVG) return -11;
    if (slot->format != FT_GLYPH_FORMAT_BITMAP && FT_Render_Glyph(slot, FT_RENDER_MODE_NORMAL)) return -5;
    const FT_Bitmap *bitmap = &slot->bitmap;
    if (!bitmap->width || !bitmap->rows) return 0;
    if (!bitmap->buffer || !bitmap->pitch || bitmap->width > 16384 || bitmap->rows > 16384) return -6;
    float scale = font->bitmap_scale;
    float left = origin_x + slot->bitmap_left * scale;
    float top = origin_y - slot->bitmap_top * scale;
    int first_x = (int)floorf(left), first_y = (int)floorf(top);
    int last_x = (int)ceilf(left + bitmap->width * scale), last_y = (int)ceilf(top + bitmap->rows * scale);
    if (first_x < 0) first_x = 0;
    if (first_y < 0) first_y = 0;
    if (last_x > width) last_x = width;
    if (last_y > height) last_y = height;
    unsigned red = rgba >> 24, green = (rgba >> 16) & 255, blue = (rgba >> 8) & 255, alpha = rgba & 255;
    int color = bitmap->pixel_mode == FT_PIXEL_MODE_BGRA;
    for (int destination_y = first_y; destination_y < last_y; destination_y++)
        for (int destination_x = first_x; destination_x < last_x; destination_x++) {
            unsigned pixel[4];
            if (scale == 1) {
                if (bitmap_pixel(bitmap, destination_x - (int)left, destination_y - (int)top, pixel) < 0) return -7;
            } else {
                // Centered bilinear resampling for fixed-strike color/bitmap fonts.
                float source_x = (destination_x + 0.5f - left) / scale - 0.5f;
                float source_y = (destination_y + 0.5f - top) / scale - 0.5f;
                int sx = (int)floorf(source_x), sy = (int)floorf(source_y);
                float fx = source_x - sx, fy = source_y - sy;
                unsigned samples[4][4];
                if (bitmap_pixel(bitmap, sx, sy, samples[0]) < 0 || bitmap_pixel(bitmap, sx + 1, sy, samples[1]) < 0 ||
                    bitmap_pixel(bitmap, sx, sy + 1, samples[2]) < 0 || bitmap_pixel(bitmap, sx + 1, sy + 1, samples[3]) < 0) return -7;
                for (int channel = 0; channel < 4; channel++)
                    pixel[channel] = (unsigned)lroundf((samples[0][channel] * (1 - fx) + samples[1][channel] * fx) * (1 - fy) +
                        (samples[2][channel] * (1 - fx) + samples[3][channel] * fx) * fy);
            }
            if (!color) {
                unsigned coverage = multiply(pixel[3], alpha);
                pixel[0] = multiply(red, coverage); pixel[1] = multiply(green, coverage);
                pixel[2] = multiply(blue, coverage); pixel[3] = coverage;
            } else for (int channel = 0; channel < 4; channel++) pixel[channel] = multiply(pixel[channel], alpha);
            source_over(destination + ((size_t)destination_y * width + destination_x) * 4,
                pixel[0], pixel[1], pixel[2], pixel[3]);
        }
    return color;
}

int bengine_font_draw_run(void *handle, float size, const uint32_t *glyphs, const float *xy,
    int count, float origin_x, float baseline, uint32_t rgba, uint8_t *destination, int width, int height) {
    if (!glyphs || !xy || count < 0 || count > 1048576) return -1;
    int colored = 0;
    for (int index = 0; index < count; index++) {
        int result = bengine_font_draw_glyph(handle, glyphs[index], size,
            origin_x + xy[index * 2], baseline + xy[index * 2 + 1], rgba, destination, width, height);
        if (result < 0) return result;
        colored |= result;
    }
    return colored;
}
