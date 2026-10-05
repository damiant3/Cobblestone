#include <fstream>
#include <vector>
#include "pol/crypt/blowfish.h"
int main(int argc, char** argv) {
    if (argc != 2) return 2;
    Pol::Crypt::BlowFish cipher;
    cipher.Init();
    std::ofstream output(argv[1], std::ios::binary);
    for (int i = 0; i < 231397; ++i) {
        unsigned char in = static_cast<unsigned char>(i * 73 + 19);
        unsigned char out;
        cipher.Decrypt(&in, &out, 1);
        output.put(static_cast<char>(out));
    }
    return output.good() ? 0 : 3;
}

