#include <dirent.h>
#include <fcntl.h>
#include <stdint.h>
#include <sys/stat.h>
#include <unistd.h>

struct clone_stat_info {
    uint32_t mode;
    uint32_t user;
    int64_t size;
};

/* Keep variadic calls and platform-specific stat/dirent layouts inside C. */
int clone_open(const char *path, int flags, int mode) { return open(path, flags, mode); }
int clone_openat(int fd, const char *path, int flags, int mode) { return openat(fd, path, flags, mode); }
int clone_getpath(int fd, int command, char *path) { return fcntl(fd, command, path); }
int clone_stat(int fd, struct clone_stat_info *result) {
    struct stat value;
    int status = fstat(fd, &value);
    if (status == 0) {
        result->mode = value.st_mode;
        result->user = value.st_uid;
        result->size = value.st_size;
    }
    return status;
}
void *clone_opendir(int fd) { return fdopendir(fd); }
const char *clone_readdir(void *listing) {
    struct dirent *entry = readdir(listing);
    return entry ? entry->d_name : NULL;
}
