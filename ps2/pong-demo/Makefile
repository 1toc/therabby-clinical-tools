EE_BIN = THERABBY_PONG.ELF
EE_OBJS = main.o
EE_INCS += -I$(GSKIT)/include
EE_LDFLAGS += -L$(GSKIT)/lib
EE_LIBS = -lpad -lgsKit -ldmakit -lm
EE_CFLAGS += -O2 -Wall -Wextra

all: $(EE_BIN)

clean:
	rm -f $(EE_BIN) $(EE_OBJS)

dist: $(EE_BIN)
	rm -rf dist
	mkdir -p dist/APPS/THERABBY_PONG
	cp $(EE_BIN) dist/APPS/THERABBY_PONG/$(EE_BIN)
	cp title.cfg dist/APPS/THERABBY_PONG/title.cfg
	cp README.md dist/README.md

include $(PS2SDK)/samples/Makefile.pref
include $(PS2SDK)/samples/Makefile.eeglobal
