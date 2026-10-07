#include <fstream>
#include "pol/crypt/blowfish.h"
class ReplayCipher : public Pol::Crypt::BlowFish {
public:
    unsigned char encode(unsigned char plain) {
        unsigned char zero = 0, pad = 0;
        Decrypt(&zero, &pad, 1);
        unsigned char cipher = plain ^ pad;
        game_seed[(block_pos + 7) & 7] = cipher;
        return cipher;
    }
};
int main(int argc, char** argv) {
    if (argc != 3) return 2;
    std::ifstream input(argv[1], std::ios::binary);
    std::ofstream output(argv[2], std::ios::binary);
    if (!input || !output) return 3;
    ReplayCipher cipher;
    cipher.Init();
    char value;
    while (input.get(value)) output.put(static_cast<char>(cipher.encode(static_cast<unsigned char>(value))));
    return input.eof() && output.good() ? 0 : 4;
}

