/*
 * lvpx_yuv_constants exposes the addresses of the libyuv YuvConstants objects.
 *
 * libyuv exports the color matrices as data symbols. A statically linked iOS or Mac Catalyst binary cannot
 * resolve data symbols with dlsym, so the managed decoder asks this function for the addresses instead.
 * The index order matches the order of the constants in managed YuvConversionMatrix:
 *   0 kYuvI601Constants, 1 kYuvJPEGConstants, 2 kYuvH709Constants, 3 kYuvF709Constants,
 *   4 kYvuI601Constants, 5 kYvuJPEGConstants, 6 kYvuH709Constants, 7 kYvuF709Constants
 */

struct YuvConstants;

extern const struct YuvConstants kYuvI601Constants;
extern const struct YuvConstants kYuvJPEGConstants;
extern const struct YuvConstants kYuvH709Constants;
extern const struct YuvConstants kYuvF709Constants;
extern const struct YuvConstants kYvuI601Constants;
extern const struct YuvConstants kYvuJPEGConstants;
extern const struct YuvConstants kYvuH709Constants;
extern const struct YuvConstants kYvuF709Constants;

/* Returns the address of the requested YuvConstants object, or 0 when the index is out of range. */
const void* lvpx_yuv_constants(int index)
{
	switch (index)
	{
		case 0: return &kYuvI601Constants;
		case 1: return &kYuvJPEGConstants;
		case 2: return &kYuvH709Constants;
		case 3: return &kYuvF709Constants;
		case 4: return &kYvuI601Constants;
		case 5: return &kYvuJPEGConstants;
		case 6: return &kYvuH709Constants;
		case 7: return &kYvuF709Constants;
		default: return 0;
	}
}
