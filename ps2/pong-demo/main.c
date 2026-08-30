#include <tamtypes.h>
#include <kernel.h>
#include <sifrpc.h>
#include <loadfile.h>
#include <libpad.h>
#include <gsKit.h>
#include <dmaKit.h>

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define PAD_PORT_1 0
#define PAD_PORT_2 1
#define PAD_SLOT 0
#define WIN_SCORE 7
#define SAFE_X 34.0f
#define SAFE_Y 26.0f
#define PADDLE_W 14.0f
#define BALL_SIZE 12.0f

#define COL_BG GS_SETREG_RGBAQ(5, 10, 18, 0x80, 0)
#define COL_LINE GS_SETREG_RGBAQ(30, 54, 68, 0x80, 0)
#define COL_P1 GS_SETREG_RGBAQ(45, 220, 235, 0x80, 0)
#define COL_P2 GS_SETREG_RGBAQ(255, 185, 55, 0x80, 0)
#define COL_BALL GS_SETREG_RGBAQ(245, 245, 235, 0x80, 0)
#define COL_TEXT GS_SETREG_RGBAQ(210, 225, 230, 0x80, 0)
#define COL_DIM GS_SETREG_RGBAQ(95, 115, 125, 0x80, 0)
#define COL_GOOD GS_SETREG_RGBAQ(90, 235, 145, 0x80, 0)

static char pad_buf_1[256] __attribute__((aligned(64)));
static char pad_buf_2[256] __attribute__((aligned(64)));

typedef struct {
    u32 held;
    u32 pressed;
    int connected;
    int analog_y;
} PadState;

typedef enum {
    STATE_TITLE = 0,
    STATE_PLAYING,
    STATE_PAUSED,
    STATE_GAMEOVER
} GameState;

typedef struct { float x, y, w, h; } Rect;
typedef struct { float x, y, vx, vy, size; } Ball;

static GSGLOBAL *gs_global;
static int screen_w;
static int screen_h;
static int video_fps;
static PadState pad1;
static PadState pad2;
static u32 old_pad_1;
static u32 old_pad_2;
static GameState game_state;
static Rect paddle_1;
static Rect paddle_2;
static Ball ball;
static int score_1;
static int score_2;
static int winner;
static int two_player;
static int speed_level;
static int paddle_level;
static int serve_wait;
static int frame_counter;

typedef struct { char ch; u8 row[7]; } Glyph;
static const Glyph glyphs[] = {
    {'A',{14,17,17,31,17,17,17}}, {'B',{30,17,17,30,17,17,30}},
    {'C',{14,17,16,16,16,17,14}}, {'D',{30,17,17,17,17,17,30}},
    {'E',{31,16,16,30,16,16,31}}, {'F',{31,16,16,30,16,16,16}},
    {'G',{14,17,16,23,17,17,14}}, {'H',{17,17,17,31,17,17,17}},
    {'I',{31,4,4,4,4,4,31}}, {'J',{7,2,2,2,18,18,12}},
    {'K',{17,18,20,24,20,18,17}}, {'L',{16,16,16,16,16,16,31}},
    {'M',{17,27,21,21,17,17,17}}, {'N',{17,25,21,19,17,17,17}},
    {'O',{14,17,17,17,17,17,14}}, {'P',{30,17,17,30,16,16,16}},
    {'Q',{14,17,17,17,21,18,13}}, {'R',{30,17,17,30,20,18,17}},
    {'S',{15,16,16,14,1,1,30}}, {'T',{31,4,4,4,4,4,4}},
    {'U',{17,17,17,17,17,17,14}}, {'V',{17,17,17,17,17,10,4}},
    {'W',{17,17,17,21,21,21,10}}, {'X',{17,17,10,4,10,17,17}},
    {'Y',{17,17,10,4,4,4,4}}, {'Z',{31,1,2,4,8,16,31}},
    {'0',{14,17,19,21,25,17,14}}, {'1',{4,12,4,4,4,4,14}},
    {'2',{14,17,1,2,4,8,31}}, {'3',{30,1,1,14,1,1,30}},
    {'4',{2,6,10,18,31,2,2}}, {'5',{31,16,16,30,1,1,30}},
    {'6',{14,16,16,30,17,17,14}}, {'7',{31,1,2,4,8,8,8}},
    {'8',{14,17,17,14,17,17,14}}, {'9',{14,17,17,15,1,1,14}},
    {':',{0,4,4,0,4,4,0}}, {'-',{0,0,0,31,0,0,0}},
    {'/',{1,2,4,8,16,0,0}}, {'.',{0,0,0,0,0,12,12}},
    {' ',{0,0,0,0,0,0,0}}
};

static void load_modules(void) {
    if (SifLoadModule("rom0:SIO2MAN", 0, NULL) < 0) SleepThread();
    if (SifLoadModule("rom0:PADMAN", 0, NULL) < 0) SleepThread();
}

static void wait_pad_ready(int port) {
    int state;
    do { state = padGetState(port, PAD_SLOT); }
    while (state != PAD_STATE_STABLE && state != PAD_STATE_FINDCTP1 && state != PAD_STATE_DISCONN);
}

static int open_pad(int port, char *buffer) {
    int modes, i;
    if (!padPortOpen(port, PAD_SLOT, buffer)) return 0;
    wait_pad_ready(port);
    if (padGetState(port, PAD_SLOT) == PAD_STATE_DISCONN) return 0;
    modes = padInfoMode(port, PAD_SLOT, PAD_MODETABLE, -1);
    for (i = 0; i < modes; ++i) {
        if (padInfoMode(port, PAD_SLOT, PAD_MODETABLE, i) == PAD_TYPE_DUALSHOCK) {
            padSetMainMode(port, PAD_SLOT, PAD_MMODE_DUALSHOCK, PAD_MMODE_LOCK);
            wait_pad_ready(port);
            break;
        }
    }
    return 1;
}

static void read_pad(int port, PadState *out, u32 *old) {
    struct padButtonStatus buttons;
    int state = padGetState(port, PAD_SLOT);
    u32 current = 0;
    out->connected = (state == PAD_STATE_STABLE || state == PAD_STATE_FINDCTP1);
    out->analog_y = 128;
    if (out->connected && padRead(port, PAD_SLOT, &buttons) != 0) {
        current = 0xffff ^ buttons.btns;
        out->analog_y = buttons.ljoy_v;
    }
    out->held = current;
    out->pressed = current & ~(*old);
    *old = current;
}

static const u8 *find_glyph(char ch) {
    size_t i;
    for (i = 0; i < sizeof(glyphs) / sizeof(glyphs[0]); ++i)
        if (glyphs[i].ch == ch) return glyphs[i].row;
    return glyphs[sizeof(glyphs) / sizeof(glyphs[0]) - 1].row;
}

static float text_width(const char *text, float scale) { return (float)strlen(text) * 6.0f * scale; }

static void draw_text(float x, float y, float scale, u64 color, const char *text) {
    const char *p;
    for (p = text; *p; ++p) {
        const u8 *rows = find_glyph(*p);
        int ry, rx;
        for (ry = 0; ry < 7; ++ry)
            for (rx = 0; rx < 5; ++rx)
                if (rows[ry] & (1 << (4 - rx))) {
                    float px = x + rx * scale;
                    float py = y + ry * scale;
                    gsKit_prim_sprite(gs_global, px, py, px + scale, py + scale, 2, color);
                }
        x += 6.0f * scale;
    }
}

static void draw_text_center(float y, float scale, u64 color, const char *text) {
    draw_text((screen_w - text_width(text, scale)) * 0.5f, y, scale, color, text);
}

static void draw_rect(const Rect *r, u64 color) {
    gsKit_prim_sprite(gs_global, r->x, r->y, r->x + r->w, r->y + r->h, 2, color);
}

static float paddle_height(void) {
    static const float sizes[3] = {68.0f, 94.0f, 126.0f};
    return sizes[paddle_level];
}

static float base_ball_speed(void) {
    static const float speeds[3] = {4.0f, 5.4f, 6.8f};
    return speeds[speed_level] * ((video_fps == 50) ? 1.2f : 1.0f);
}

static void reset_paddles(void) {
    float ph = paddle_height();
    paddle_1.x = SAFE_X + 10.0f;
    paddle_1.y = (screen_h - ph) * 0.5f;
    paddle_1.w = PADDLE_W;
    paddle_1.h = ph;
    paddle_2.x = screen_w - SAFE_X - 10.0f - PADDLE_W;
    paddle_2.y = (screen_h - ph) * 0.5f;
    paddle_2.w = PADDLE_W;
    paddle_2.h = ph;
}

static void reset_ball(int direction) {
    float s = base_ball_speed();
    ball.x = screen_w * 0.5f - BALL_SIZE * 0.5f;
    ball.y = screen_h * 0.5f - BALL_SIZE * 0.5f;
    ball.vx = (direction >= 0 ? 1.0f : -1.0f) * s;
    ball.vy = ((score_1 + score_2) & 1 ? 0.58f : -0.58f) * s;
    ball.size = BALL_SIZE;
    serve_wait = video_fps / 2;
}

static void start_match(void) {
    score_1 = score_2 = winner = frame_counter = 0;
    reset_paddles();
    reset_ball(-1);
    game_state = STATE_PLAYING;
}

static float clampf(float v, float lo, float hi) {
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

static float pad_axis(const PadState *pad) {
    float axis = 0.0f;
    if (pad->held & PAD_UP) axis -= 1.0f;
    if (pad->held & PAD_DOWN) axis += 1.0f;
    if (pad->analog_y < 96) axis -= (96 - pad->analog_y) / 96.0f;
    if (pad->analog_y > 160) axis += (pad->analog_y - 160) / 95.0f;
    return clampf(axis, -1.0f, 1.0f);
}

static int overlaps(float ax, float ay, float aw, float ah, const Rect *b) {
    return ax < b->x + b->w && ax + aw > b->x && ay < b->y + b->h && ay + ah > b->y;
}

static void update_playing(void) {
    float paddle_speed = (video_fps == 50) ? 8.4f : 7.0f;
    float a2;
    paddle_1.y += pad_axis(&pad1) * paddle_speed;
    paddle_1.y = clampf(paddle_1.y, SAFE_Y, screen_h - SAFE_Y - paddle_1.h);
    if (two_player && pad2.connected) {
        a2 = pad_axis(&pad2);
        paddle_2.y += a2 * paddle_speed;
    } else {
        float target = ball.y + ball.size * 0.5f - paddle_2.h * 0.5f;
        float cpu_speed = paddle_speed * (0.52f + speed_level * 0.10f);
        if (ball.vx > 0 || ball.x > screen_w * 0.58f) {
            if (target > paddle_2.y + 5.0f) paddle_2.y += cpu_speed;
            if (target < paddle_2.y - 5.0f) paddle_2.y -= cpu_speed;
        }
    }
    paddle_2.y = clampf(paddle_2.y, SAFE_Y, screen_h - SAFE_Y - paddle_2.h);
    if (pad1.pressed & PAD_START) { game_state = STATE_PAUSED; return; }
    if (pad1.pressed & PAD_SELECT) { game_state = STATE_TITLE; return; }
    if (serve_wait > 0) { --serve_wait; return; }
    ball.x += ball.vx;
    ball.y += ball.vy;
    if (ball.y <= SAFE_Y) { ball.y = SAFE_Y; ball.vy = fabsf(ball.vy); }
    if (ball.y + ball.size >= screen_h - SAFE_Y) {
        ball.y = screen_h - SAFE_Y - ball.size;
        ball.vy = -fabsf(ball.vy);
    }
    if (ball.vx < 0 && overlaps(ball.x, ball.y, ball.size, ball.size, &paddle_1)) {
        float relative = ((ball.y + ball.size * 0.5f) - (paddle_1.y + paddle_1.h * 0.5f)) / (paddle_1.h * 0.5f);
        float speed = base_ball_speed() * (1.0f + 0.025f * (score_1 + score_2));
        ball.x = paddle_1.x + paddle_1.w;
        ball.vx = fabsf(speed);
        ball.vy = clampf(relative, -1.0f, 1.0f) * speed * 0.90f;
    }
    if (ball.vx > 0 && overlaps(ball.x, ball.y, ball.size, ball.size, &paddle_2)) {
        float relative = ((ball.y + ball.size * 0.5f) - (paddle_2.y + paddle_2.h * 0.5f)) / (paddle_2.h * 0.5f);
        float speed = base_ball_speed() * (1.0f + 0.025f * (score_1 + score_2));
        ball.x = paddle_2.x - ball.size;
        ball.vx = -fabsf(speed);
        ball.vy = clampf(relative, -1.0f, 1.0f) * speed * 0.90f;
    }
    if (ball.x + ball.size < 0.0f) {
        ++score_2;
        if (score_2 >= WIN_SCORE) { winner = 2; game_state = STATE_GAMEOVER; }
        else reset_ball(-1);
    } else if (ball.x > screen_w) {
        ++score_1;
        if (score_1 >= WIN_SCORE) { winner = 1; game_state = STATE_GAMEOVER; }
        else reset_ball(1);
    }
}

static void update_state(void) {
    if (game_state == STATE_TITLE) {
        if (pad1.pressed & PAD_LEFT) speed_level = (speed_level + 2) % 3;
        if (pad1.pressed & PAD_RIGHT) speed_level = (speed_level + 1) % 3;
        if (pad1.pressed & PAD_SQUARE) paddle_level = (paddle_level + 1) % 3;
        if (pad1.pressed & PAD_TRIANGLE) two_player = !two_player;
        if (pad1.pressed & PAD_CROSS) start_match();
    } else if (game_state == STATE_PLAYING) update_playing();
    else if (game_state == STATE_PAUSED) {
        if (pad1.pressed & PAD_START) game_state = STATE_PLAYING;
        if (pad1.pressed & PAD_SELECT) game_state = STATE_TITLE;
    } else if (game_state == STATE_GAMEOVER) {
        if (pad1.pressed & PAD_CROSS) start_match();
        if (pad1.pressed & PAD_SELECT) game_state = STATE_TITLE;
    }
}

static void draw_center_line(void) {
    float y;
    for (y = SAFE_Y; y < screen_h - SAFE_Y; y += 24.0f)
        gsKit_prim_sprite(gs_global, screen_w * 0.5f - 2.0f, y, screen_w * 0.5f + 2.0f, y + 12.0f, 1, COL_LINE);
}

static void draw_score(void) {
    char buf[8];
    snprintf(buf, sizeof(buf), "%d", score_1);
    draw_text(screen_w * 0.5f - 70.0f - text_width(buf, 5.0f), SAFE_Y + 8.0f, 5.0f, COL_P1, buf);
    snprintf(buf, sizeof(buf), "%d", score_2);
    draw_text(screen_w * 0.5f + 70.0f, SAFE_Y + 8.0f, 5.0f, COL_P2, buf);
}

static void render_title(void) {
    char line[64];
    draw_text_center(52.0f, 6.0f, COL_TEXT, "THERABBY PONG");
    draw_text_center(116.0f, 2.2f, COL_DIM, "PS2 CLINICAL DEMO");
    snprintf(line, sizeof(line), "MODE: %s", two_player ? "2P" : "1P CPU");
    draw_text_center(174.0f, 2.4f, two_player ? COL_P2 : COL_P1, line);
    snprintf(line, sizeof(line), "SPEED: %d", speed_level + 1);
    draw_text_center(207.0f, 2.4f, COL_TEXT, line);
    snprintf(line, sizeof(line), "PADDLE: %d", paddle_level + 1);
    draw_text_center(240.0f, 2.4f, COL_TEXT, line);
    draw_text_center(296.0f, 2.0f, COL_GOOD, "CROSS START");
    draw_text_center(326.0f, 1.7f, COL_DIM, "TRIANGLE 1P 2P");
    draw_text_center(351.0f, 1.7f, COL_DIM, "LEFT RIGHT SPEED");
    draw_text_center(376.0f, 1.7f, COL_DIM, "SQUARE PADDLE");
    draw_text_center(410.0f, 1.4f, COL_DIM, "UP DOWN OR LEFT STICK");
}

static void render_game(void) {
    Rect br = { ball.x, ball.y, ball.size, ball.size };
    draw_center_line();
    draw_rect(&paddle_1, COL_P1);
    draw_rect(&paddle_2, COL_P2);
    draw_rect(&br, COL_BALL);
    draw_score();
    if (serve_wait > 0) draw_text_center(screen_h * 0.5f - 12.0f, 2.4f, COL_DIM, "READY");
    if (game_state == STATE_PAUSED) {
        gsKit_prim_sprite(gs_global, 120, screen_h * 0.5f - 45, screen_w - 120, screen_h * 0.5f + 45, 3, GS_SETREG_RGBAQ(8, 14, 20, 0x80, 0));
        draw_text_center(screen_h * 0.5f - 14.0f, 3.2f, COL_TEXT, "PAUSED");
    }
}

static void render_gameover(void) {
    render_game();
    gsKit_prim_sprite(gs_global, 85, screen_h * 0.5f - 72, screen_w - 85, screen_h * 0.5f + 78, 3, GS_SETREG_RGBAQ(8, 14, 20, 0x80, 0));
    draw_text_center(screen_h * 0.5f - 48.0f, 3.0f, winner == 1 ? COL_P1 : COL_P2, winner == 1 ? "PLAYER 1 WINS" : (two_player ? "PLAYER 2 WINS" : "CPU WINS"));
    draw_text_center(screen_h * 0.5f + 5.0f, 1.8f, COL_TEXT, "CROSS REMATCH");
    draw_text_center(screen_h * 0.5f + 35.0f, 1.6f, COL_DIM, "SELECT MENU");
}

static void render(void) {
    gsKit_clear(gs_global, COL_BG);
    if (game_state == STATE_TITLE) render_title();
    else if (game_state == STATE_GAMEOVER) render_gameover();
    else render_game();
    gsKit_queue_exec(gs_global);
    gsKit_sync_flip(gs_global);
}

static void init_graphics(void) {
    gs_global = gsKit_init_global();
    gs_global->PSM = GS_PSM_CT24;
    gs_global->PSMZ = GS_PSMZ_16S;
    gs_global->ZBuffering = GS_SETTING_OFF;
    gs_global->DoubleBuffering = GS_SETTING_ON;
    gs_global->PrimAlphaEnable = GS_SETTING_ON;
    gs_global->Dithering = GS_SETTING_OFF;
    gs_global->Mode = gsKit_check_rom();
    if (gs_global->Mode == GS_MODE_PAL) { gs_global->Height = 512; video_fps = 50; }
    else { gs_global->Height = 448; video_fps = 60; }
    gs_global->Width = 640;
    screen_w = gs_global->Width;
    screen_h = gs_global->Height;
    dmaKit_init(D_CTRL_RELE_OFF, D_CTRL_MFD_OFF, D_CTRL_STS_UNSPEC, D_CTRL_STD_OFF, D_CTRL_RCYC_8, 1 << DMA_CHANNEL_GIF);
    dmaKit_chan_init(DMA_CHANNEL_GIF);
    gsKit_vram_clear(gs_global);
    gsKit_init_screen(gs_global);
    gsKit_mode_switch(gs_global, GS_ONESHOT);
}

int main(int argc, char *argv[]) {
    (void)argc; (void)argv;
    SifInitRpc(0);
    load_modules();
    padInit(0);
    open_pad(PAD_PORT_1, pad_buf_1);
    open_pad(PAD_PORT_2, pad_buf_2);
    init_graphics();
    game_state = STATE_TITLE;
    two_player = 0;
    speed_level = 1;
    paddle_level = 1;
    old_pad_1 = old_pad_2 = 0;
    for (;;) {
        read_pad(PAD_PORT_1, &pad1, &old_pad_1);
        read_pad(PAD_PORT_2, &pad2, &old_pad_2);
        update_state();
        render();
        ++frame_counter;
    }
    return 0;
}
