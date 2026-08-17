#include <stdio.h>

int main(int argc, char* argv[]) {
    printf("argc = %d\n", argc);
    for (int i = 0; i < argc - 1; i++) {
        int j = 0;
        while (argv[i][j] != '\0') {
            printf("%c", argv[i][j]);
            j++;
        }
        printf("\n");
    }
    return 0;
}